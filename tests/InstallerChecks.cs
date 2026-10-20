using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class InstallerChecks
{
    private static int passed, failed;
    private static string work, original, bundle1, bundle2, originalHash, output;
    private static readonly string[] Steps = { "transaction-published", "after-assembly", "after-plugin", "after-manifest" };

    private sealed class Fixture
    {
        internal string Game, Managed, State, Assembly, Plugin, Manifest, Backup, Transaction;
        internal Fixture(string label)
        {
            Game = Path.Combine(work, label + "-" + Guid.NewGuid().ToString("N"));
            Managed = Path.Combine(Game, "DawnOfMan_Data", "Managed");
            State = Path.Combine(Game, "DawnTrainer");
            Assembly = Path.Combine(Managed, "Assembly-CSharp.dll");
            Plugin = Path.Combine(Managed, "DawnOfManTrainer.dll");
            Manifest = Path.Combine(State, "installed.sha256");
            Backup = Path.Combine(State, "Assembly-CSharp.dll.original");
            Transaction = Path.Combine(State, ".transaction");
            Directory.CreateDirectory(Managed);
            File.Copy(original, Assembly);
        }
        internal string[] Files { get { return new[] { Assembly, Plugin, Manifest }; } }
        internal string Snapshot() { return String.Join("|", Files.Select(Hash).ToArray()); }
    }

    private static string Hash(string path)
    {
        if (!File.Exists(path)) return "-";
        using (SHA256 sha = SHA256.Create()) using (FileStream file = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
    }

    private static void Check(bool condition, string label)
    {
        if (condition) { passed++; Console.WriteLine("PASS " + label); }
        else { failed++; Console.WriteLine("FAIL " + label + "\n" + output); }
    }

    private static void Configure(string bundle)
    {
        Installer.TestOriginalHash = delegate(string hash) { return hash == originalHash; };
        Installer.TestBundle = bundle;
        Installer.TestStep = null;
    }

    private static int Run(Fixture fixture, string bundle, params string[] flags)
    {
        Installer.TestBundle = bundle;
        TextWriter oldOut = Console.Out, oldError = Console.Error;
        using (StringWriter capture = new StringWriter())
        {
            try
            {
                Console.SetOut(capture); Console.SetError(capture);
                return Installer.Run(new[] { fixture.Game }.Concat(flags).ToArray());
            }
            finally { output = capture.ToString(); Console.SetOut(oldOut); Console.SetError(oldError); }
        }
    }

    private static MethodDefinition AddMethod(ModuleDefinition module, TypeDefinition type, string name, bool boolean, params TypeReference[] parameters)
    {
        MethodDefinition method = new MethodDefinition(name, MethodAttributes.Public, boolean ? module.TypeSystem.Boolean : module.TypeSystem.Void);
        foreach (TypeReference parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
        if (boolean) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method); return method;
    }

    private static MethodDefinition AddConstructor(ModuleDefinition module, TypeDefinition type, MethodReference parent)
    {
        MethodDefinition constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, parent ?? module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes))));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); type.Methods.Add(constructor); return constructor;
    }

    private static FieldDefinition AddField(TypeDefinition type, string name, TypeReference fieldType, FieldAttributes attributes)
    {
        FieldDefinition field = new FieldDefinition(name, attributes, fieldType); type.Fields.Add(field); return field;
    }

    private static void Body(MethodDefinition method, params Instruction[] instructions)
    { method.Body.Instructions.Clear(); foreach (Instruction instruction in instructions) method.Body.Instructions.Add(instruction); }

    private static void AddReturnBranches(MethodDefinition method)
    {
        Instruction first = Instruction.Create(OpCodes.Ret), second = Instruction.Create(OpCodes.Ret);
        Body(method, Instruction.Create(method.ReturnType.FullName == "System.Int32" ? OpCodes.Ldc_I4_3 : OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Switch, new[] { first, second }),
            Instruction.Create(OpCodes.Br_S, first), first, second);
    }

    private static void BuildMultiplierGame(ModuleDefinition module)
    {
        TypeDefinition physical = AddType(module, "DawnOfMan.PhysicalEntity");
        MethodDefinition physicalConstructor = AddConstructor(module, physical, null);
        TypeDefinition incoming = AddType(module, "DawnOfMan.PhysicalEntityType"); AddConstructor(module, incoming, null);
        TypeDefinition slotType = new TypeDefinition("DawnOfMan", "SlotType", TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
        module.Types.Add(slotType); AddField(slotType, "value__", module.TypeSystem.Int32, FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName);
        for (int index = 0; index < 3; index++)
        {
            FieldDefinition value = AddField(slotType, "Value" + index, slotType, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault);
            value.Constant = index;
        }
        MethodDefinition slots = new MethodDefinition("countEmptySlots", MethodAttributes.Public, module.TypeSystem.Int32);
        slots.Parameters.Add(new ParameterDefinition(slotType)); physical.Methods.Add(slots); AddReturnBranches(slots);
        AddReturnBranches(AddMethod(module, physical, "anyEmptySlots", true, slotType));

        TypeDefinition definition = AddType(module, "DawnOfMan.SlotDefinition");
        MethodDefinition definitionConstructor = AddConstructor(module, definition, null);
        FieldDefinition capacity = AddField(definition, "Capacity", module.TypeSystem.Int32, FieldAttributes.Public);
        FieldDefinition slotCount = AddField(definition, "Count", module.TypeSystem.Int32, FieldAttributes.Public);
        TypeDefinition slot = AddType(module, "DawnOfMan.Slot");
        FieldDefinition definitionField = AddField(slot, "mSlotDefinition", definition, FieldAttributes.Private);
        MethodDefinition slotConstructor = AddConstructor(module, slot, null); Instruction last = slotConstructor.Body.Instructions.Last();
        ILProcessor ctor = slotConstructor.Body.GetILProcessor();
        ctor.InsertBefore(last, Instruction.Create(OpCodes.Ldarg_0)); ctor.InsertBefore(last, Instruction.Create(OpCodes.Newobj, definitionConstructor));
        ctor.InsertBefore(last, Instruction.Create(OpCodes.Dup)); ctor.InsertBefore(last, Instruction.Create(OpCodes.Ldc_I4_5));
        ctor.InsertBefore(last, Instruction.Create(OpCodes.Stfld, capacity)); ctor.InsertBefore(last, Instruction.Create(OpCodes.Stfld, definitionField));
        foreach (string name in new[] { "getCapacity", "getSpaceLeft" })
        {
            MethodDefinition getter = new MethodDefinition(name, MethodAttributes.Public, module.TypeSystem.Int32); slot.Methods.Add(getter);
            Body(getter, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, definitionField), Instruction.Create(OpCodes.Ldfld, capacity), Instruction.Create(OpCodes.Ret));
            if (name == "getSpaceLeft")
            {
                Instruction ret = Instruction.Create(OpCodes.Ret);
                Body(getter, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, definitionField), Instruction.Create(OpCodes.Ldfld, capacity),
                    Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, definitionField), Instruction.Create(OpCodes.Ldfld, slotCount),
                    Instruction.Create(OpCodes.Switch, new[] { ret }), Instruction.Create(OpCodes.Br_S, ret), ret);
            }
        }
        AddMethod(module, slot, "isFull", true);
        MethodDefinition canStore = AddMethod(module, slot, "canStoreEntity", true, incoming);
        Body(canStore, Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldnull), Instruction.Create(OpCodes.Cgt_Un), Instruction.Create(OpCodes.Ret));

        TypeDefinition being = AddType(module, "DawnOfMan.Being"); being.BaseType = physical;
        MethodDefinition beingConstructor = AddConstructor(module, being, physicalConstructor);
        TypeDefinition human = AddType(module, "DawnOfMan.Human"); human.BaseType = being; AddConstructor(module, human, beingConstructor);
        foreach (string name in new[] { "DawnOfMan.AiRuleGoGetResourceToStore", "DawnOfMan.AiRuleTaskGetResource" })
        {
            TypeDefinition rule = AddType(module, name); AddConstructor(module, rule, null);
            FieldDefinition firstSlots = AddField(rule, "FirstSlots", module.TypeSystem.Int32, FieldAttributes.Public), secondSlots = AddField(rule, "SecondSlots", module.TypeSystem.Int32, FieldAttributes.Public);
            MethodDefinition run = AddMethod(module, rule, "run", true, being);
            Body(run, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Callvirt, slots), Instruction.Create(OpCodes.Stfld, firstSlots),
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldc_I4_1), Instruction.Create(OpCodes.Callvirt, slots), Instruction.Create(OpCodes.Stfld, secondSlots),
                Instruction.Create(OpCodes.Ldc_I4_1), Instruction.Create(OpCodes.Ret));
            if (name == "DawnOfMan.AiRuleTaskGetResource")
            {
                MethodDefinition canPerform = AddMethod(module, rule, "canPerform", true, human);
                Body(canPerform, Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Callvirt, physical.Methods.Single(method => method.Name == "anyEmptySlots")), Instruction.Create(OpCodes.Ret));
            }
        }
        TypeDefinition deadBody = AddType(module, "DawnOfMan.AiRuleGoGetDeadBody"); AddConstructor(module, deadBody, null);
        MethodDefinition bodyRun = AddMethod(module, deadBody, "run", true, being);
        Body(bodyRun, Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Callvirt, slots), Instruction.Create(OpCodes.Ldc_I4_3), Instruction.Create(OpCodes.Ceq), Instruction.Create(OpCodes.Ret));
        MethodDefinition bodyCan = AddMethod(module, deadBody, "canPerform", true, human);
        Body(bodyCan, Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Callvirt, physical.Methods.Single(method => method.Name == "anyEmptySlots")), Instruction.Create(OpCodes.Ret));
        TypeDefinition equipment = AddType(module, "DawnOfMan.AiRuleTaskGetTool"); AddConstructor(module, equipment, null);
        MethodDefinition toolCan = AddMethod(module, equipment, "canPerform", true, human);
        Body(toolCan, Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Callvirt, physical.Methods.Single(method => method.Name == "anyEmptySlots")), Instruction.Create(OpCodes.Ret));
        TypeDefinition buildable = AddType(module, "DawnOfMan.Buildable"); buildable.BaseType = physical;
        MethodDefinition buildableConstructor = AddConstructor(module, buildable, physicalConstructor);
        TypeDefinition state = AddType(module, "DawnOfMan.BeingState");
        FieldDefinition owner = AddField(state, "mBeing", being, FieldAttributes.Family);
        MethodDefinition stateConstructor = AddConstructor(module, state, null); last = stateConstructor.Body.Instructions.Last(); ctor = stateConstructor.Body.GetILProcessor();
        ctor.InsertBefore(last, Instruction.Create(OpCodes.Ldarg_0)); ctor.InsertBefore(last, Instruction.Create(OpCodes.Newobj, beingConstructor)); ctor.InsertBefore(last, Instruction.Create(OpCodes.Stfld, owner));
        TypeDefinition operation = AddType(module, "DawnOfMan.BeingStateBuildableOperation"); operation.BaseType = state;
        MethodDefinition operationConstructor = AddConstructor(module, operation, stateConstructor);
        FieldDefinition target = AddField(operation, "NativeTarget", buildable, FieldAttributes.Public);
        last = operationConstructor.Body.Instructions.Last(); ctor = operationConstructor.Body.GetILProcessor();
        ctor.InsertBefore(last, Instruction.Create(OpCodes.Ldarg_0)); ctor.InsertBefore(last, Instruction.Create(OpCodes.Newobj, buildableConstructor)); ctor.InsertBefore(last, Instruction.Create(OpCodes.Stfld, target));
        MethodDefinition getBuildable = new MethodDefinition("getBuildable", MethodAttributes.Family, buildable); operation.Methods.Add(getBuildable);
        Body(getBuildable, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, target), Instruction.Create(OpCodes.Ret));
        TypeDefinition building = AddType(module, "DawnOfMan.BeingStateBuilding"); building.BaseType = operation; AddConstructor(module, building, operationConstructor);
        FieldDefinition nativeWork = AddField(building, "NativeWork", module.TypeSystem.Single, FieldAttributes.Public);
        FieldDefinition nativeCalls = AddField(building, "NativeCalls", module.TypeSystem.Int32, FieldAttributes.Public);
        MethodDefinition increase = AddMethod(module, building, "increaseBuildingOperation", false, module.TypeSystem.Single); increase.IsPublic = false; increase.IsFamily = true;
        Body(increase, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Stfld, nativeWork),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Dup), Instruction.Create(OpCodes.Ldfld, nativeCalls), Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add), Instruction.Create(OpCodes.Stfld, nativeCalls), Instruction.Create(OpCodes.Ret));
    }

    private static MethodDefinition HookMethod(ModuleDefinition module, TypeDefinition trainer, string name, TypeReference returns, params TypeReference[] parameters)
    {
        MethodDefinition method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, returns);
        foreach (TypeReference parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
        trainer.Methods.Add(method); return method;
    }

    private static void HookCounter(ModuleDefinition module, TypeDefinition trainer, MethodDefinition method, string field)
    {
        FieldDefinition counter = AddField(trainer, field, module.TypeSystem.Int32, FieldAttributes.Public | FieldAttributes.Static);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, counter)); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Add)); method.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, counter));
    }

    private static void BuildMultiplierHooks(ModuleDefinition module, ModuleDefinition game, TypeDefinition trainer)
    {
        TypeReference slot = module.ImportReference(game.GetType("DawnOfMan.Slot")), incoming = module.ImportReference(game.GetType("DawnOfMan.PhysicalEntityType"));
        TypeReference physical = module.ImportReference(game.GetType("DawnOfMan.PhysicalEntity")), slotType = module.ImportReference(game.GetType("DawnOfMan.SlotType"));
        TypeReference buildable = module.ImportReference(game.GetType("DawnOfMan.Buildable"));
        MethodDefinition prepare = HookMethod(module, trainer, "PrepareSlot", module.TypeSystem.Void, slot, incoming); HookCounter(module, trainer, prepare, "PrepareCalls");
        FieldDefinition lastIncoming = AddField(trainer, "LastIncoming", incoming, FieldAttributes.Public | FieldAttributes.Static);
        prepare.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1)); prepare.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, lastIncoming)); prepare.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        MethodDefinition plan = HookMethod(module, trainer, "ResourceCarrySlots", module.TypeSystem.Int32, physical, slotType); HookCounter(module, trainer, plan, "PlanCalls");
        plan.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); plan.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        plan.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, module.ImportReference(game.GetType("DawnOfMan.PhysicalEntity").Methods.Single(method => method.Name == "countEmptySlots"))));
        plan.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)10));
        plan.Body.Instructions.Add(Instruction.Create(OpCodes.Add)); plan.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        MethodDefinition any = HookMethod(module, trainer, "ResourceCarrySpace", module.TypeSystem.Boolean, physical, slotType); HookCounter(module, trainer, any, "SpaceCalls");
        any.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); any.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        any.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, module.ImportReference(game.GetType("DawnOfMan.PhysicalEntity").Methods.Single(method => method.Name == "anyEmptySlots"))));
        any.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        any.Body.Instructions.Add(Instruction.Create(OpCodes.Ceq)); any.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        MethodDefinition nonnegative = HookMethod(module, trainer, "NonnegativeSpace", module.TypeSystem.Int32, module.TypeSystem.Int32);
        Instruction positive = Instruction.Create(OpCodes.Ldarg_0);
        Body(nonnegative, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Bge_S, positive),
            Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Ret), positive, Instruction.Create(OpCodes.Ret));
        MethodDefinition scale = HookMethod(module, trainer, "ScaleBuildWork", module.TypeSystem.Single, module.TypeSystem.Single, physical, buildable); HookCounter(module, trainer, scale, "ScaleCalls");
        FieldDefinition lastOwner = AddField(trainer, "LastOwner", physical, FieldAttributes.Public | FieldAttributes.Static), lastTarget = AddField(trainer, "LastTarget", buildable, FieldAttributes.Public | FieldAttributes.Static);
        scale.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1)); scale.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, lastOwner));
        scale.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2)); scale.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, lastTarget));
        scale.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); scale.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 4f));
        scale.Body.Instructions.Add(Instruction.Create(OpCodes.Mul)); scale.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }

    private static TypeDefinition AddType(ModuleDefinition module, string fullName)
    {
        int split = fullName.LastIndexOf('.');
        TypeDefinition type = new TypeDefinition(fullName.Substring(0, split), fullName.Substring(split + 1), TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(type); return type;
    }

    private static void BuildFixtures()
    {
        original = Path.Combine(work, "fixture-original.dll");
        using (AssemblyDefinition game = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp", new Version(1, 0)), "Assembly-CSharp", ModuleKind.Dll))
        {
            ModuleDefinition module = game.MainModule;
            TypeDefinition behaviour = AddType(module, "DawnOfMan.GameBehaviour");
            AddMethod(module, behaviour, "Start", false); AddMethod(module, behaviour, "Update", false);
            TypeDefinition gui = AddType(module, "DawnOfMan.GameGuiManager");
            AddMethod(module, gui, "updateInput", false); AddMethod(module, gui, "updateSpeedInputs", false);
            TypeDefinition camera = AddType(module, "DawnOfMan.CameraModeInteractive");
            AddMethod(module, camera, "update", false); AddMethod(module, camera, "fixedUpdate", false);
            AddMethod(module, AddType(module, "MadrugaShared.GuiManager"), "onEvent", false);
            TypeDefinition stat = AddType(module, "DawnOfMan.Stat");
            AddMethod(module, stat, "decay", true); AddMethod(module, stat, "setValue", false, module.TypeSystem.Single);
            BuildMultiplierGame(module);
            game.Write(original);
        }
        originalHash = Hash(original);
        bundle1 = Path.Combine(work, "bundle-1"); bundle2 = Path.Combine(work, "bundle-2");
        foreach (string bundle in new[] { bundle1, bundle2 })
        {
            Directory.CreateDirectory(bundle);
            using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(original))
            using (AssemblyDefinition mod = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("DawnOfManTrainer", new Version(bundle == bundle1 ? 1 : 2, 0)), "DawnOfManTrainer", ModuleKind.Dll))
            {
                ModuleDefinition module = mod.MainModule;
                TypeDefinition trainer = AddType(module, "DawnTrainer.Trainer");
                AddMethod(module, trainer, "Bootstrap", false); AddMethod(module, trainer, "Frame", false);
                AddMethod(module, trainer, "BlockInput", true);
                AddMethod(module, trainer, "IsLocked", true, module.ImportReference(game.MainModule.GetType("DawnOfMan.Stat")));
                BuildMultiplierHooks(module, game.MainModule, trainer);
                foreach (MethodDefinition method in trainer.Methods) { method.IsStatic = true; method.HasThis = false; }
                mod.Write(Path.Combine(bundle, "DawnOfManTrainer.dll"));
            }
        }
        Configure(bundle1);
    }

    private static void CheckInstalled(Fixture fixture, string bundle, string label)
    {
        Check(Hash(fixture.Plugin) == Hash(Path.Combine(bundle, "DawnOfManTrainer.dll")) &&
            File.ReadAllText(fixture.Manifest).Trim() == Hash(fixture.Assembly) && Hash(fixture.Backup) == originalHash &&
            !Directory.Exists(fixture.Transaction) && Run(fixture, bundle, "--verify") == 0, label);
    }

    private static void SuccessChecks()
    {
        Fixture fixture = new Fixture("success"); string before = fixture.Snapshot();
        Check(Run(fixture, bundle1, "--verify") == 0 && fixture.Snapshot() == before && !Directory.Exists(fixture.State), "fresh verify is read-only");
        Check(Run(fixture, bundle1) == 0, "fresh install completes");
        CheckInstalled(fixture, bundle1, "fresh install records matching library and assembly");
        DateTime backupWritten = File.GetLastWriteTimeUtc(fixture.Backup);
        string firstInstall = fixture.Snapshot();
        Check(Run(fixture, bundle2, "--verify") == 1 && fixture.Snapshot() == firstInstall, "verify rejects other package without changing installed files");
        Check(Run(fixture, bundle2) == 0 && fixture.Snapshot() != firstInstall, "update installs a different library and repatches original");
        CheckInstalled(fixture, bundle2, "update records matching library and assembly");
        Check(File.GetLastWriteTimeUtc(fixture.Backup) == backupWritten, "update never overwrites original backup");
        string installed = fixture.Snapshot();
        Check(Run(fixture, bundle2, "--uninstall", "--verify") == 0 && fixture.Snapshot() == installed, "uninstall verify is read-only");
        Check(Run(fixture, bundle2, "--uninstall") == 0 && fixture.Snapshot() == before && Hash(fixture.Backup) == originalHash, "uninstall restores original and removes library and manifest");
        Check(Run(fixture, bundle2, "--uninstall") == 0 && fixture.Snapshot() == before, "repeated uninstall is safe");
    }

    private static void ExceptionChecks()
    {
        foreach (string operation in new[] { "install", "update", "uninstall" }) foreach (string step in Steps)
        {
            Fixture fixture = new Fixture("exception-" + operation);
            if (operation != "install" && Run(fixture, bundle1) != 0) throw new Exception("Fixture installation failed: " + output);
            string before = fixture.Snapshot();
            Installer.TestStep = delegate(string name) { if (name == step) throw new IOException("Injected failure at " + name); };
            int result = Run(fixture, bundle2, operation == "uninstall" ? new[] { "--uninstall" } : new string[0]);
            Installer.TestStep = null;
            Check(result == 1 && fixture.Snapshot() == before && !Directory.Exists(fixture.Transaction) && Hash(fixture.Backup) == originalHash,
                operation + " exception at " + step + " restores all three targets and retains original backup");
        }
    }

    private static int Interrupt(Fixture fixture, string operation, string step)
    {
        string arguments = "--child \"" + fixture.Game + "\" \"" + bundle2 + "\" " + operation + " " + step + " " + originalHash;
        using (Process child = Process.Start(new ProcessStartInfo
        {
            FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
            Arguments = arguments, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }))
        {
            output = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
            if (!child.WaitForExit(30000)) { child.Kill(); throw new Exception("Interrupted installer child timed out."); }
            return child.ExitCode;
        }
    }

    private static void InterruptionChecks()
    {
        foreach (string operation in new[] { "install", "update", "uninstall" }) foreach (string step in Steps)
        {
            Fixture fixture = new Fixture("interrupt-" + operation);
            if (operation != "install" && Run(fixture, bundle1) != 0) throw new Exception("Fixture installation failed: " + output);
            string before = fixture.Snapshot();
            Check(Interrupt(fixture, operation, step) == 77 && Directory.Exists(fixture.Transaction), operation + " process interruption at " + step + " retains durable journal");
            string interrupted = fixture.Snapshot();
            Check(Run(fixture, bundle2, "--verify") == 1 && fixture.Snapshot() == interrupted && Directory.Exists(fixture.Transaction), "pending " + operation + " verify is read-only at " + step);
            Installer.TestStep = delegate(string name) { if (name == "recovered") throw new IOException("Stop after completed recovery"); };
            int recovered = Run(fixture, bundle2);
            Installer.TestStep = null;
            Check(recovered == 1 && fixture.Snapshot() == before && !Directory.Exists(fixture.Transaction), "next normal run restores pre-" + operation + " files at " + step);
            Check(Run(fixture, bundle2, "--uninstall") == 0 && Hash(fixture.Assembly) == originalHash && !File.Exists(fixture.Plugin) && !File.Exists(fixture.Manifest), "uninstall remains usable after interrupted " + operation + " at " + step);
        }
    }

    private static void ProtectionChecks()
    {
        Fixture corrupt = new Fixture("corrupt-backup");
        Directory.CreateDirectory(corrupt.State); File.WriteAllText(corrupt.Backup, "keep this invalid backup");
        string before = corrupt.Snapshot(), backup = Hash(corrupt.Backup);
        Check(Run(corrupt, bundle1) == 1 && corrupt.Snapshot() == before && Hash(corrupt.Backup) == backup, "invalid existing backup is never overwritten");

        Fixture anotherVersion = new Fixture("other-original-backup");
        Directory.CreateDirectory(anotherVersion.State); File.WriteAllText(anotherVersion.Backup, "another accepted original fixture");
        before = anotherVersion.Snapshot(); backup = Hash(anotherVersion.Backup);
        Installer.TestOriginalHash = delegate(string hash) { return hash == originalHash || hash == backup; };
        Check(Run(anotherVersion, bundle1) == 1 && anotherVersion.Snapshot() == before && Hash(anotherVersion.Backup) == backup,
            "backup for another accepted game version is preserved and blocks install");
        Configure(bundle1);

        string invalidBundle = Path.Combine(work, "bundle-missing-hook"); Directory.CreateDirectory(invalidBundle);
        using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Path.Combine(bundle1, "DawnOfManTrainer.dll")))
        {
            TypeDefinition type = mod.MainModule.GetType("DawnTrainer.Trainer");
            type.Methods.Remove(type.Methods.Single(method => method.Name == "BlockInput"));
            mod.Write(Path.Combine(invalidBundle, "DawnOfManTrainer.dll"));
        }
        foreach (bool installed in new[] { false, true })
        {
            Fixture invalidHook = new Fixture("missing-hook");
            if (installed && Run(invalidHook, bundle1) != 0) throw new Exception(output);
            before = invalidHook.Snapshot();
            Check(Run(invalidHook, invalidBundle) == 1 && invalidHook.Snapshot() == before && !Directory.Exists(invalidHook.Transaction),
                "missing hook rejects " + (installed ? "update" : "install") + " before changing any target");
        }

        Fixture unknown = new Fixture("unknown-game-file"); File.WriteAllText(unknown.Assembly, "third-party game assembly");
        before = unknown.Snapshot();
        Check(Run(unknown, bundle1) == 1 && unknown.Snapshot() == before && !File.Exists(unknown.Backup), "unknown game assembly is preserved before backup or patching");

        Fixture locked = new Fixture("locked-installer");
        Directory.CreateDirectory(locked.State); before = locked.Snapshot();
        using (FileStream held = new FileStream(Path.Combine(locked.State, ".installer.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            Check(Run(locked, bundle1) == 1 && locked.Snapshot() == before, "concurrent installer is refused before target changes");

        foreach (string operation in new[] { "update", "uninstall" })
        {
            Fixture lockedPlugin = new Fixture("locked-plugin");
            if (Run(lockedPlugin, bundle1) != 0) throw new Exception(output);
            before = lockedPlugin.Snapshot();
            using (FileStream held = new FileStream(lockedPlugin.Plugin, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(Run(lockedPlugin, bundle2, operation == "uninstall" ? new[] { "--uninstall" } : new string[0]) == 1 &&
                    lockedPlugin.Snapshot() == before && !Directory.Exists(lockedPlugin.Transaction),
                    "actual plugin file lock during " + operation + " rolls back already-replaced assembly");
        }

        Fixture blockedRollback = new Fixture("blocked-rollback");
        if (Run(blockedRollback, bundle1) != 0) throw new Exception(output);
        before = blockedRollback.Snapshot(); FileStream blocked = null;
        Installer.TestStep = delegate(string name)
        {
            if (name != "after-plugin") return;
            blocked = new FileStream(blockedRollback.Assembly, FileMode.Open, FileAccess.Read, FileShare.Read);
            throw new IOException("Injected failure with assembly held during rollback");
        };
        try
        {
            Check(Run(blockedRollback, bundle2) == 1 && Directory.Exists(blockedRollback.Transaction),
                "actual assembly lock preventing rollback retains journal and snapshots");
        }
        finally { if (blocked != null) blocked.Dispose(); Installer.TestStep = null; }
        Installer.TestStep = delegate(string name) { if (name == "recovered") throw new IOException("Stop after completed recovery"); };
        int retried = Run(blockedRollback, bundle2); Installer.TestStep = null;
        Check(retried == 1 && blockedRollback.Snapshot() == before && !Directory.Exists(blockedRollback.Transaction),
            "next normal run recovers after rollback file lock is released");

        Fixture changed = new Fixture("external-change");
        if (Run(changed, bundle1) != 0) throw new Exception(output);
        Check(Interrupt(changed, "update", "after-plugin") == 77, "prepare interrupted update for external edit protection");
        File.WriteAllText(changed.Plugin, "external user content"); before = changed.Snapshot();
        Check(Run(changed, bundle2, "--uninstall") == 1 && changed.Snapshot() == before && Directory.Exists(changed.Transaction), "external edit prevents all automatic recovery writes and retains journal");

        Fixture damaged = new Fixture("damaged-snapshot");
        Check(Interrupt(damaged, "install", "after-assembly") == 77, "prepare interrupted install for snapshot corruption protection");
        File.WriteAllText(Path.Combine(damaged.Transaction, "assembly.before"), "damaged snapshot"); before = damaged.Snapshot();
        Check(Run(damaged, bundle2) == 1 && damaged.Snapshot() == before && Directory.Exists(damaged.Transaction), "damaged recovery snapshot stops before changing any target");

        Fixture rollback = new Fixture("interrupted-rollback");
        if (Run(rollback, bundle1) != 0) throw new Exception(output);
        before = rollback.Snapshot();
        Check(Interrupt(rollback, "update", "after-plugin") == 77 && Interrupt(rollback, "update", "rollback-assembly") == 77,
            "recovery may itself be interrupted after restoring assembly");
        Installer.TestStep = delegate(string name) { if (name == "recovered") throw new IOException("Stop after completed recovery"); };
        int result = Run(rollback, bundle2); Installer.TestStep = null;
        Check(result == 1 && rollback.Snapshot() == before && !Directory.Exists(rollback.Transaction), "second recovery still has intact snapshots and restores all targets");
    }

    private static string MethodShape(MethodDefinition method)
    {
        return String.Join("|", method.Body.Instructions.Select(instruction =>
        {
            Instruction branch = instruction.Operand as Instruction; Instruction[] arms = instruction.Operand as Instruction[];
            string operand = branch != null ? "branch:" + method.Body.Instructions.IndexOf(branch) : arms != null ?
                String.Join(",", arms.Select(target => method.Body.Instructions.IndexOf(target).ToString()).ToArray()) :
                instruction.Operand == null ? "" : instruction.Operand.ToString();
            return instruction.OpCode.Name + ":" + operand;
        }).ToArray());
    }

    private static int ExecuteHooks(Fixture fixture)
    {
        using (Process child = Process.Start(new ProcessStartInfo
        {
            FileName = System.Reflection.Assembly.GetExecutingAssembly().Location, Arguments = "--execute \"" + fixture.Game + "\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        }))
        {
            output = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
            if (!child.WaitForExit(30000)) { child.Kill(); throw new Exception("Hook execution child timed out."); }
            return child.ExitCode;
        }
    }

    private static int HookExecution(string gamePath)
    {
        try
        {
            string managed = Path.Combine(gamePath, "DawnOfMan_Data", "Managed");
            System.Reflection.Assembly game = System.Reflection.Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            System.Reflection.Assembly mod = System.Reflection.Assembly.LoadFrom(Path.Combine(managed, "DawnOfManTrainer.dll"));
            Type trainer = mod.GetType("DawnTrainer.Trainer"), physical = game.GetType("DawnOfMan.PhysicalEntity"), enumType = game.GetType("DawnOfMan.SlotType");
            object owner = Activator.CreateInstance(physical);
            foreach (int value in new[] { 0, 1, 2 })
            {
                object type = Enum.ToObject(enumType, value);
                Check((int)physical.GetMethod("countEmptySlots").Invoke(owner, new[] { type }) == 3 &&
                    !(bool)physical.GetMethod("anyEmptySlots").Invoke(owner, new[] { type }), "native general slot queries retain their original return paths " + value);
            }
            Check((int)trainer.GetField("PlanCalls").GetValue(null) == 0 && (int)trainer.GetField("SpaceCalls").GetValue(null) == 0, "general slot queries never enter resource wrappers");
            object being = Activator.CreateInstance(game.GetType("DawnOfMan.Being")), human = Activator.CreateInstance(game.GetType("DawnOfMan.Human"));
            foreach (string name in new[] { "DawnOfMan.AiRuleGoGetResourceToStore", "DawnOfMan.AiRuleTaskGetResource" })
            {
                Type ruleType = game.GetType(name); object rule = Activator.CreateInstance(ruleType);
                Check((bool)ruleType.GetMethod("run").Invoke(rule, new[] { being }) && (int)ruleType.GetField("FirstSlots").GetValue(rule) == 13 &&
                    (int)ruleType.GetField("SecondSlots").GetValue(rule) == 13, "resource run executes both replacement callsites " + name);
                if (name == "DawnOfMan.AiRuleTaskGetResource") Check((bool)ruleType.GetMethod("canPerform").Invoke(rule, new[] { human }), "resource eligibility executes resource space wrapper");
            }
            Check((int)trainer.GetField("PlanCalls").GetValue(null) == 4 && (int)trainer.GetField("SpaceCalls").GetValue(null) == 1, "each selected resource callsite runs exactly once");
            Type dead = game.GetType("DawnOfMan.AiRuleGoGetDeadBody"); object bodyRule = Activator.CreateInstance(dead);
            Check((bool)dead.GetMethod("run").Invoke(bodyRule, new[] { being }) && !(bool)dead.GetMethod("canPerform").Invoke(bodyRule, new[] { human }), "body hauling preserves native count and availability");
            Type equipment = game.GetType("DawnOfMan.AiRuleTaskGetTool");
            Check(!(bool)equipment.GetMethod("canPerform").Invoke(Activator.CreateInstance(equipment), new[] { human }) &&
                (int)trainer.GetField("SpaceCalls").GetValue(null) == 1, "equipment rules preserve native availability");

            Type slotType = game.GetType("DawnOfMan.Slot"); object slot = Activator.CreateInstance(slotType);
            Check((int)slotType.GetMethod("getCapacity").Invoke(slot, null) == 5 && !(bool)slotType.GetMethod("isFull").Invoke(slot, null) &&
                (int)slotType.GetMethod("getSpaceLeft").Invoke(slot, null) == 5 && (int)trainer.GetField("PrepareCalls").GetValue(null) == 3 &&
                trainer.GetField("LastIncoming").GetValue(null) == null, "all three native slot getters prepare with null incoming");
            object incoming = Activator.CreateInstance(game.GetType("DawnOfMan.PhysicalEntityType"));
            Check((bool)slotType.GetMethod("canStoreEntity").Invoke(slot, new[] { incoming }) &&
                Object.ReferenceEquals(trainer.GetField("LastIncoming").GetValue(null), incoming) && (int)trainer.GetField("PrepareCalls").GetValue(null) == 4,
                "slot acceptance prepares with exact incoming and preserves native result");
            Check(!(bool)slotType.GetMethod("canStoreEntity").Invoke(slot, new object[] { null }), "null incoming retains native refusal");
            object definition = slotType.GetField("mSlotDefinition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(slot);
            definition.GetType().GetField("Capacity").SetValue(definition, -7);
            Check((int)slotType.GetMethod("getSpaceLeft").Invoke(slot, null) == 0, "switch targeting shared ret still clamps overfull native space");
            definition.GetType().GetField("Count").SetValue(definition, 1);
            Check((int)slotType.GetMethod("getSpaceLeft").Invoke(slot, null) == 0 && (int)definition.GetType().GetField("Count").GetValue(definition) == 1,
                "branch targeting shared ret still clamps space and leaves count unchanged");
            Type building = game.GetType("DawnOfMan.BeingStateBuilding"); object state = Activator.CreateInstance(building);
            System.Reflection.MethodInfo increase = building.GetMethod("increaseBuildingOperation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            increase.Invoke(state, new object[] { 2.5f });
            Check((float)building.GetField("NativeWork").GetValue(state) == 10f && (int)building.GetField("NativeCalls").GetValue(state) == 1 &&
                (int)trainer.GetField("ScaleCalls").GetValue(null) == 1 && trainer.GetField("LastOwner").GetValue(null).GetType().FullName == "DawnOfMan.Being" &&
                Object.ReferenceEquals(trainer.GetField("LastTarget").GetValue(null), building.GetField("NativeTarget").GetValue(state)),
                "scaled building work receives real owner/target then executes native flow once");
            increase.Invoke(state, new object[] { 1f });
            Check((float)building.GetField("NativeWork").GetValue(state) == 4f && (int)building.GetField("NativeCalls").GetValue(state) == 2 &&
                (int)trainer.GetField("ScaleCalls").GetValue(null) == 2, "repeated building call does not multiply through duplicate hooks");
        }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL executable hook test: " + exception); }
        Console.WriteLine("Executable hook checks: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void MultiplierChecks()
    {
        Fixture fixture = new Fixture("multiplier-hooks");
        foreach (string phase in new[] { "install", "update", "repeated-update" })
        {
            string bundle = phase == "install" ? bundle1 : bundle2;
            Check(Run(fixture, bundle) == 0, phase + " with multiplier hooks succeeds");
            using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(fixture.Assembly))
            using (AssemblyDefinition clean = AssemblyDefinition.ReadAssembly(original))
            {
                MethodDefinition[] hooked = game.MainModule.Types.SelectMany(type => type.Methods).Where(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                {
                    MethodReference call = instruction.Operand as MethodReference; return call != null && call.DeclaringType.FullName == "DawnTrainer.Trainer";
                })).ToArray();
                MethodReference[] calls = hooked.SelectMany(method => method.Body.Instructions).Select(instruction => instruction.Operand as MethodReference)
                    .Where(call => call != null && call.DeclaringType.FullName == "DawnTrainer.Trainer").ToArray();
                Check(hooked.Length == 16 && calls.Length == 19 && calls.Select(call => call.Name).Distinct().Count() == 9 &&
                    calls.Count(call => call.Name == "PrepareSlot") == 4 && calls.Count(call => call.Name == "ResourceCarrySlots") == 4 &&
                    calls.Count(call => call.Name == "ResourceCarrySpace") == 1 && calls.Count(call => call.Name == "NonnegativeSpace") == 1 && calls.Count(call => call.Name == "ScaleBuildWork") == 1,
                    phase + " has exactly 9 hook entries, 16 game methods and 19 calls without stacking");
                foreach (string name in new[] { "DawnOfMan.PhysicalEntity", "DawnOfMan.AiRuleGoGetDeadBody", "DawnOfMan.AiRuleTaskGetTool" })
                    foreach (MethodDefinition method in clean.MainModule.GetType(name).Methods.Where(method => method.HasBody))
                        Check(MethodShape(game.MainModule.GetType(name).Methods.Single(candidate => candidate.Name == method.Name)) == MethodShape(method), phase + " preserves native " + name + "." + method.Name);
            }
            Check(ExecuteHooks(fixture) == 0, phase + " executes every new hook and preserves body/equipment/native queries");
            Check(Run(fixture, bundle, "--verify") == 0 && output.Contains("9 个挂接入口、16 个游戏方法、19 处挂接调用"), phase + " verify reports actual hook counts");
        }

        foreach (string name in new[] { "PrepareSlot", "ResourceCarrySlots", "ResourceCarrySpace", "NonnegativeSpace", "ScaleBuildWork" })
        {
            string invalidBundle = Path.Combine(work, "missing-" + name); Directory.CreateDirectory(invalidBundle);
            using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Path.Combine(bundle2, "DawnOfManTrainer.dll")))
            {
                TypeDefinition trainer = mod.MainModule.GetType("DawnTrainer.Trainer"); trainer.Methods.Remove(trainer.Methods.Single(method => method.Name == name));
                mod.Write(Path.Combine(invalidBundle, "DawnOfManTrainer.dll"));
            }
            string before = fixture.Snapshot();
            Check(Run(fixture, invalidBundle) == 1 && fixture.Snapshot() == before && !Directory.Exists(fixture.Transaction), "missing new hook " + name + " rejects update before writes");
        }

        string[] labels = { "capacity field type", "readonly slot definition", "builder field type", "static buildable getter", "building work parameter", "building inheritance", "resource callsite count", "enum backing type" };
        Action<ModuleDefinition>[] mutations =
        {
            delegate(ModuleDefinition module) { module.GetType("DawnOfMan.SlotDefinition").Fields.Single(field => field.Name == "Capacity").FieldType = module.TypeSystem.Single; },
            delegate(ModuleDefinition module) { module.GetType("DawnOfMan.Slot").Fields.Single(field => field.Name == "mSlotDefinition").IsInitOnly = true; },
            delegate(ModuleDefinition module) { module.GetType("DawnOfMan.BeingState").Fields.Single(field => field.Name == "mBeing").FieldType = module.GetType("DawnOfMan.PhysicalEntity"); },
            delegate(ModuleDefinition module) { MethodDefinition method = module.GetType("DawnOfMan.BeingStateBuildableOperation").Methods.Single(candidate => candidate.Name == "getBuildable"); method.IsStatic = true; method.HasThis = false; },
            delegate(ModuleDefinition module) { module.GetType("DawnOfMan.BeingStateBuilding").Methods.Single(method => method.Name == "increaseBuildingOperation").Parameters[0].ParameterType = module.TypeSystem.Int32; },
            delegate(ModuleDefinition module) { module.GetType("DawnOfMan.BeingStateBuilding").BaseType = module.TypeSystem.Object; },
            delegate(ModuleDefinition module)
            {
                Instruction call = module.GetType("DawnOfMan.AiRuleTaskGetResource").Methods.Single(method => method.Name == "run").Body.Instructions.First(instruction =>
                    instruction.Operand is MethodReference && ((MethodReference)instruction.Operand).Name == "countEmptySlots");
                call.OpCode = OpCodes.Nop; call.Operand = null;
            },
            delegate(ModuleDefinition module) { module.GetType("DawnOfMan.SlotType").Fields.Single(field => field.Name == "value__").FieldType = module.TypeSystem.Int64; }
        };
        for (int index = 0; index < mutations.Length; index++)
        {
            Fixture incompatible = new Fixture("incompatible-shape");
            using (AssemblyDefinition changed = AssemblyDefinition.ReadAssembly(incompatible.Assembly, new ReaderParameters { InMemory = true }))
            { mutations[index](changed.MainModule); changed.Write(incompatible.Assembly); }
            string accepted = Hash(incompatible.Assembly), before = incompatible.Snapshot();
            Installer.TestOriginalHash = delegate(string hash) { return hash == accepted; };
            Check(Run(incompatible, bundle1) == 1 && incompatible.Snapshot() == before && !File.Exists(incompatible.Backup) && !Directory.Exists(incompatible.Transaction),
                "incompatible " + labels[index] + " rejects before backup or game writes");
            Configure(bundle1);
        }

        Fixture multipleReturns = new Fixture("multiple-return-clamp");
        using (AssemblyDefinition changed = AssemblyDefinition.ReadAssembly(multipleReturns.Assembly, new ReaderParameters { InMemory = true }))
        {
            MethodDefinition getter = changed.MainModule.GetType("DawnOfMan.Slot").Methods.Single(method => method.Name == "getSpaceLeft");
            Instruction second = Instruction.Create(OpCodes.Ret); getter.Body.Instructions.Add(second);
            Instruction branches = getter.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Switch);
            branches.Operand = new[] { ((Instruction[])branches.Operand)[0], second };
            changed.Write(multipleReturns.Assembly);
        }
        string multiHash = Hash(multipleReturns.Assembly); Installer.TestOriginalHash = delegate(string hash) { return hash == multiHash; };
        Check(Run(multipleReturns, bundle1) == 0 && ExecuteHooks(multipleReturns) == 0, "every native getSpaceLeft return is clamped, including branch and switch paths");
        Check(Run(multipleReturns, bundle1, "--verify") == 0 && output.Contains("9 个挂接入口、16 个游戏方法、20 处挂接调用"), "verify derives call count from all injected returns");
        Configure(bundle1);
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--execute") return HookExecution(args[1]);
        if (args.Length > 0 && args[0] == "--child")
        {
            originalHash = args[5]; Configure(args[2]);
            Installer.TestStep = delegate(string name) { if (name == args[4]) Environment.Exit(77); };
            return Installer.Run(args[3] == "uninstall" ? new[] { args[1], "--uninstall" } : new[] { args[1] });
        }
        try
        {
            work = Path.GetFullPath(args[0]); Directory.CreateDirectory(work);
            BuildFixtures(); SuccessChecks(); ExceptionChecks(); InterruptionChecks(); ProtectionChecks(); MultiplierChecks();
        }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL unexpected exception: " + exception); }
        Console.WriteLine("Installer checks: " + passed + " passed, " + failed + " failed. Sandbox: " + work);
        return failed == 0 ? 0 : 1;
    }
}
