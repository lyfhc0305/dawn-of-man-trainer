using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Installer
{
    private static readonly string[] OriginalHashes =
    {
        "381061C26BA34BA38C95B9F82FA17777BB9CEACA5F07B0BB5AD0A0A0FF46A170",
        "A9AE0E5E6A75CA0BE8E741045BFB57010E9B83C13814396415C256975D867FF3"
    };
    private static bool IsOriginal(string hash)
    {
#if INSTALLER_TESTS
        if (TestOriginalHash != null) return TestOriginalHash(hash);
#endif
        return OriginalHashes.Contains(hash);
    }
#if INSTALLER_TESTS
    // Compiled only into the sandbox regression runner, never into Installer.exe.
    internal static Func<string, bool> TestOriginalHash;
    internal static Action<string> TestStep;
    internal static string TestBundle;
#endif
    private static void Step(string name)
    {
#if INSTALLER_TESTS
        if (TestStep != null) TestStep(name);
#endif
    }
    internal static string ResolveGamePath(string requestedGame, string installerDirectory)
    {
        string path = requestedGame ?? @"..\DawnOfMan";
        if (Path.IsPathRooted(path))
        {
            // Drive-relative and root-relative Windows paths depend on the caller's
            // current drive. Require a complete absolute path or a normal relative one.
            if (Path.GetPathRoot(path).Length < 3)
                throw new ArgumentException("请使用相对安装器的路径，或包含完整根目录的绝对路径。", "requestedGame");
            return Path.GetFullPath(path);
        }
        return Path.GetFullPath(Path.Combine(installerDirectory, path));
    }
    private static string Hash(string file)
    { using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }

    // A completed install records only the current patched hash. The transaction journal
    // separately records both versions while an install, update, or uninstall is in flight.
    private static bool Recorded(string manifest, string hash)
    { return File.Exists(manifest) && File.ReadAllLines(manifest).Any(line => line.Trim() == hash); }

    private static MethodDefinition Method(ModuleDefinition module, string type, string method)
    { return module.GetType(type).Methods.Single(m => m.Name == method); }

    private static MethodDefinition CheckedMethod(ModuleDefinition module, string type, string name, string returns, params string[] parameters)
    {
        TypeDefinition declaring = module.GetType(type);
        MethodDefinition[] methods = declaring == null ? new MethodDefinition[0] : declaring.Methods.Where(m => m.Name == name).ToArray();
        MethodDefinition method = methods.Length == 1 ? methods[0] : null;
        if (method == null || method.IsStatic || !method.HasThis || method.HasGenericParameters || !method.HasBody || method.Body.Instructions.Count == 0 ||
            method.ReturnType.FullName != returns || !method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters))
            throw new Exception("游戏方法结构不匹配：" + type + "." + name + "，未修改任何游戏文件。");
        return method;
    }

    private static FieldDefinition CheckedField(ModuleDefinition module, string type, string name, string fieldType)
    {
        TypeDefinition declaring = module.GetType(type);
        FieldDefinition[] fields = declaring == null ? new FieldDefinition[0] : declaring.Fields.Where(f => f.Name == name).ToArray();
        FieldDefinition field = fields.Length == 1 ? fields[0] : null;
        if (field == null || field.IsStatic || field.IsInitOnly || field.FieldType.FullName != fieldType)
            throw new Exception("游戏字段结构不匹配：" + type + "." + name + "，未修改任何游戏文件。");
        return field;
    }

    private static bool DerivesFrom(ModuleDefinition module, string name, string ancestor)
    {
        for (int depth = 0; depth < 32; depth++)
        {
            if (name == ancestor) return true;
            TypeDefinition type = module.GetType(name);
            if (type == null || type.BaseType == null) return false;
            name = type.BaseType.FullName;
        }
        return false;
    }

    // The patched game calls these by exact signature; reject a library that no longer provides one.
    private static MethodReference Hook(ModuleDefinition game, ModuleDefinition mod, string name, string returns, params string[] parameters)
    {
        TypeDefinition trainer = mod.GetType("DawnTrainer.Trainer");
        MethodDefinition[] found = trainer == null || !trainer.IsPublic ? new MethodDefinition[0] : trainer.Methods.Where(m => m.Name == name).ToArray();
        MethodDefinition method = found.Length == 1 ? found[0] : null;
        if (method == null || !method.IsPublic || !method.IsStatic || method.HasThis || method.HasGenericParameters || method.ReturnType.FullName != returns
            || !method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters))
            throw new Exception("功能库缺少可用的挂接方法 " + name + "，未修改任何游戏文件。");
        return game.ImportReference(method);
    }

    private static void Prefix(MethodDefinition method, MethodReference hook)
    {
        ILProcessor il = method.Body.GetILProcessor();
        il.InsertBefore(method.Body.Instructions[0], il.Create(OpCodes.Call, hook));
    }

    private static void Gate(MethodDefinition method, MethodReference hook)
    {
        ILProcessor il = method.Body.GetILProcessor(); Instruction original = method.Body.Instructions[0];
        il.InsertBefore(original, il.Create(OpCodes.Call, hook));
        il.InsertBefore(original, il.Create(OpCodes.Brfalse, original));
        il.InsertBefore(original, il.Create(OpCodes.Ret));
    }

    private static void PrepareSlot(MethodDefinition method, MethodReference hook, bool incoming)
    {
        ILProcessor il = method.Body.GetILProcessor(); Instruction original = method.Body.Instructions[0];
        il.InsertBefore(original, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(original, il.Create(incoming ? OpCodes.Ldarg_1 : OpCodes.Ldnull));
        il.InsertBefore(original, il.Create(OpCodes.Call, hook));
    }

    private static void ExpandBranch(Instruction instruction)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Br_S: instruction.OpCode = OpCodes.Br; break;
            case Code.Brfalse_S: instruction.OpCode = OpCodes.Brfalse; break;
            case Code.Brtrue_S: instruction.OpCode = OpCodes.Brtrue; break;
            case Code.Beq_S: instruction.OpCode = OpCodes.Beq; break;
            case Code.Bge_S: instruction.OpCode = OpCodes.Bge; break;
            case Code.Bge_Un_S: instruction.OpCode = OpCodes.Bge_Un; break;
            case Code.Bgt_S: instruction.OpCode = OpCodes.Bgt; break;
            case Code.Bgt_Un_S: instruction.OpCode = OpCodes.Bgt_Un; break;
            case Code.Ble_S: instruction.OpCode = OpCodes.Ble; break;
            case Code.Ble_Un_S: instruction.OpCode = OpCodes.Ble_Un; break;
            case Code.Blt_S: instruction.OpCode = OpCodes.Blt; break;
            case Code.Blt_Un_S: instruction.OpCode = OpCodes.Blt_Un; break;
            case Code.Bne_Un_S: instruction.OpCode = OpCodes.Bne_Un; break;
            case Code.Leave_S: instruction.OpCode = OpCodes.Leave; break;
        }
    }

    private static void TransformReturns(MethodDefinition method, MethodReference hook)
    {
        // These original methods have no protected regions. Refuse a new shape rather
        // than guess where a hook belongs relative to changed exception boundaries.
        if (method.Body.HasExceptionHandlers || !method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ret))
            throw new Exception("仓储余量方法的返回结构不匹配，未修改任何游戏文件。");
        ILProcessor il = method.Body.GetILProcessor();
        foreach (Instruction ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            Instruction first = il.Create(OpCodes.Call, hook);
            // Branches (including switch arms) that shared a return must run the hook.
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (Object.ReferenceEquals(instruction.Operand, ret)) instruction.Operand = first;
                Instruction[] targets = instruction.Operand as Instruction[];
                if (targets != null) for (int index = 0; index < targets.Length; index++)
                    if (Object.ReferenceEquals(targets[index], ret)) targets[index] = first;
                ExpandBranch(instruction);
            }
            il.InsertBefore(ret, first);
        }
    }

    private static void ResourceCalls(MethodDefinition method, MethodDefinition original, MethodReference hook, int expected)
    {
        Instruction[] calls = method.Body.Instructions.Where(instruction =>
        {
            MethodReference reference = instruction.Operand as MethodReference;
            return (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && reference != null && reference.FullName == original.FullName;
        }).ToArray();
        if (calls.Length != expected || calls.Any(call => call.Previous != null && call.Previous.OpCode.OpCodeType == OpCodeType.Prefix))
            throw new Exception("普通资源搬运调用结构不匹配：" + method.FullName + "，未修改任何游戏文件。");
        foreach (Instruction call in calls) { call.OpCode = OpCodes.Call; call.Operand = hook; }
    }

    private static void MultiplierHooks(ModuleDefinition module, ModuleDefinition mod)
    {
        MethodReference prepare = Hook(module, mod, "PrepareSlot", "System.Void", "DawnOfMan.Slot", "DawnOfMan.PhysicalEntityType");
        MethodReference plan = Hook(module, mod, "ResourceCarrySlots", "System.Int32", "DawnOfMan.PhysicalEntity", "DawnOfMan.SlotType");
        MethodReference space = Hook(module, mod, "ResourceCarrySpace", "System.Boolean", "DawnOfMan.PhysicalEntity", "DawnOfMan.SlotType");
        MethodReference nonnegative = Hook(module, mod, "NonnegativeSpace", "System.Int32", "System.Int32");
        MethodReference scale = Hook(module, mod, "ScaleBuildWork", "System.Single", "System.Single", "DawnOfMan.PhysicalEntity", "DawnOfMan.Buildable");
        TypeDefinition definition = module.GetType("DawnOfMan.SlotDefinition"), slotType = module.GetType("DawnOfMan.SlotType");
        FieldDefinition slotDefinition = CheckedField(module, "DawnOfMan.Slot", "mSlotDefinition", "DawnOfMan.SlotDefinition");
        FieldDefinition capacity = CheckedField(module, "DawnOfMan.SlotDefinition", "Capacity", "System.Int32");
        FieldDefinition count = CheckedField(module, "DawnOfMan.SlotDefinition", "Count", "System.Int32");
        if (definition == null || definition.IsValueType || !slotDefinition.IsPrivate || !capacity.IsPublic || !count.IsPublic ||
            slotType == null || !slotType.IsEnum || slotType.Fields.Single(f => f.Name == "value__").FieldType.FullName != "System.Int32")
            throw new Exception("仓储容量字段或搬运类型结构不匹配，未修改任何游戏文件。");

        MethodDefinition getCapacity = CheckedMethod(module, "DawnOfMan.Slot", "getCapacity", "System.Int32");
        MethodDefinition isFull = CheckedMethod(module, "DawnOfMan.Slot", "isFull", "System.Boolean");
        MethodDefinition getSpaceLeft = CheckedMethod(module, "DawnOfMan.Slot", "getSpaceLeft", "System.Int32");
        MethodDefinition canStore = CheckedMethod(module, "DawnOfMan.Slot", "canStoreEntity", "System.Boolean", "DawnOfMan.PhysicalEntityType");
        MethodDefinition countSlots = CheckedMethod(module, "DawnOfMan.PhysicalEntity", "countEmptySlots", "System.Int32", "DawnOfMan.SlotType");
        MethodDefinition anySpace = CheckedMethod(module, "DawnOfMan.PhysicalEntity", "anyEmptySlots", "System.Boolean", "DawnOfMan.SlotType");
        MethodDefinition build = CheckedMethod(module, "DawnOfMan.BeingStateBuilding", "increaseBuildingOperation", "System.Void", "System.Single");
        MethodDefinition getBuildable = CheckedMethod(module, "DawnOfMan.BeingStateBuildableOperation", "getBuildable", "DawnOfMan.Buildable");
        FieldDefinition being = CheckedField(module, "DawnOfMan.BeingState", "mBeing", "DawnOfMan.Being");
        if (!being.IsFamily || !getBuildable.IsFamily ||
            !DerivesFrom(module, "DawnOfMan.BeingStateBuilding", "DawnOfMan.BeingStateBuildableOperation") ||
            !DerivesFrom(module, "DawnOfMan.BeingStateBuildableOperation", "DawnOfMan.BeingState") ||
            !DerivesFrom(module, "DawnOfMan.Being", "DawnOfMan.PhysicalEntity") ||
            !DerivesFrom(module, "DawnOfMan.Buildable", "DawnOfMan.PhysicalEntity"))
            throw new Exception("建造状态继承或访问结构不匹配，未修改任何游戏文件。");

        PrepareSlot(getCapacity, prepare, false); PrepareSlot(isFull, prepare, false); PrepareSlot(getSpaceLeft, prepare, false); PrepareSlot(canStore, prepare, true);
        TransformReturns(getSpaceLeft, nonnegative);
        ResourceCalls(CheckedMethod(module, "DawnOfMan.AiRuleGoGetResourceToStore", "run", "System.Boolean", "DawnOfMan.Being"), countSlots, plan, 2);
        ResourceCalls(CheckedMethod(module, "DawnOfMan.AiRuleTaskGetResource", "run", "System.Boolean", "DawnOfMan.Being"), countSlots, plan, 2);
        ResourceCalls(CheckedMethod(module, "DawnOfMan.AiRuleTaskGetResource", "canPerform", "System.Boolean", "DawnOfMan.Human"), anySpace, space, 1);
        ILProcessor il = build.Body.GetILProcessor(); Instruction original = build.Body.Instructions[0];
        il.InsertBefore(original, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(original, il.Create(OpCodes.Ldarg_0)); il.InsertBefore(original, il.Create(OpCodes.Ldfld, being));
        il.InsertBefore(original, il.Create(OpCodes.Ldarg_0)); il.InsertBefore(original, il.Create(OpCodes.Call, getBuildable));
        il.InsertBefore(original, il.Create(OpCodes.Call, scale)); il.InsertBefore(original, il.Create(OpCodes.Starg, build.Parameters[0]));
    }

    private static string HookSummary(ModuleDefinition module)
    {
        MethodDefinition[] methods = module.Types.SelectMany(type => type.Methods).Where(method => method.HasBody && method.Body.Instructions.Any(instruction =>
        {
            MethodReference reference = instruction.Operand as MethodReference;
            return reference != null && reference.DeclaringType.FullName == "DawnTrainer.Trainer";
        })).ToArray();
        MethodReference[] calls = methods.SelectMany(method => method.Body.Instructions).Select(instruction => instruction.Operand as MethodReference)
            .Where(reference => reference != null && reference.DeclaringType.FullName == "DawnTrainer.Trainer").ToArray();
        return calls.Select(reference => reference.Name).Distinct().Count() + " 个挂接入口、" + methods.Length + " 个游戏方法、" + calls.Length + " 处挂接调用";
    }

    private static void Patch(ModuleDefinition module, ModuleDefinition mod)
    {
        MethodReference bootstrap = Hook(module, mod, "Bootstrap", "System.Void"), frame = Hook(module, mod, "Frame", "System.Void");
        MethodReference blockInput = Hook(module, mod, "BlockInput", "System.Boolean"), isLocked = Hook(module, mod, "IsLocked", "System.Boolean", "DawnOfMan.Stat");
        Prefix(Method(module, "DawnOfMan.GameBehaviour", "Start"), bootstrap);
        Prefix(Method(module, "DawnOfMan.GameBehaviour", "Update"), frame);
        Gate(Method(module, "DawnOfMan.GameGuiManager", "updateInput"), blockInput);
        Gate(Method(module, "DawnOfMan.GameGuiManager", "updateSpeedInputs"), blockInput);
        Gate(Method(module, "DawnOfMan.CameraModeInteractive", "update"), blockInput);
        Gate(Method(module, "DawnOfMan.CameraModeInteractive", "fixedUpdate"), blockInput);
        Gate(Method(module, "MadrugaShared.GuiManager", "onEvent"), blockInput);
        MethodDefinition decay = Method(module, "DawnOfMan.Stat", "decay");
        ILProcessor il = decay.Body.GetILProcessor(); Instruction original = decay.Body.Instructions[0];
        il.InsertBefore(original, il.Create(OpCodes.Ldarg_0)); il.InsertBefore(original, il.Create(OpCodes.Call, isLocked));
        il.InsertBefore(original, il.Create(OpCodes.Brfalse, original));
        il.InsertBefore(original, il.Create(OpCodes.Ldarg_0)); il.InsertBefore(original, il.Create(OpCodes.Ldc_R4, 1f));
        il.InsertBefore(original, il.Create(OpCodes.Call, Method(module, "DawnOfMan.Stat", "setValue")));
        il.InsertBefore(original, il.Create(OpCodes.Ldc_I4_0)); il.InsertBefore(original, il.Create(OpCodes.Ret));
        MultiplierHooks(module, mod);
    }

    private sealed class TransactionFile
    {
        internal string Name, Target, Before, After;
    }

    private static string FileHash(string path) { return File.Exists(path) ? Hash(path) : "-"; }
    private static bool ValidHash(string value)
    { return value == "-" || (value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))); }

    private static void CopyDurable(string source, string destination)
    {
        using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { input.CopyTo(output); output.Flush(true); }
    }

    private static void WriteDurable(string path, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        using (FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
    }

    // Keep a private copy of each snapshot: File.Replace consumes its source, and a
    // second interrupted recovery must still be able to restore the same old files.
    private static void SetFile(string source, string target, string expectedHash)
    {
        if (expectedHash == "-")
        { if (File.Exists(target)) File.Delete(target); return; }
        string temporary = Path.Combine(Path.GetDirectoryName(target), ".DawnTrainer-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            CopyDurable(source, temporary);
            if (Hash(temporary) != expectedHash) throw new IOException("事务暂存文件校验失败。");
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Transaction directories contain only our fixed-name files. Do not recursively
    // remove an unexpected subdirectory, even when cleaning up a completed operation.
    private static void DeleteTransactionDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }

    private static TransactionFile[] TransactionFiles(string managed, string state)
    {
        return new[]
        {
            new TransactionFile { Name = "assembly", Target = Path.Combine(managed, "Assembly-CSharp.dll") },
            new TransactionFile { Name = "plugin", Target = Path.Combine(managed, "DawnOfManTrainer.dll") },
            new TransactionFile { Name = "manifest", Target = Path.Combine(state, "installed.sha256") }
        };
    }

    private static TransactionFile[] ReadTransaction(string directory, string managed, string state)
    {
        string[] lines = File.ReadAllLines(Path.Combine(directory, "journal"));
        TransactionFile[] files = TransactionFiles(managed, state);
        if (lines.Length != files.Length + 1 || lines[0] != "DawnTrainer transaction 1")
            throw new IOException("未完成安装事务的记录损坏，请保留 DawnTrainer 目录后检查。");
        for (int index = 0; index < files.Length; index++)
        {
            string[] parts = lines[index + 1].Split('|');
            if (parts.Length != 3 || parts[0] != files[index].Name || !ValidHash(parts[1]) || !ValidHash(parts[2]))
                throw new IOException("未完成安装事务的记录损坏，请保留 DawnTrainer 目录后检查。");
            files[index].Before = parts[1]; files[index].After = parts[2];
            if (files[index].Before != "-" && FileHash(Path.Combine(directory, files[index].Name + ".before")) != files[index].Before)
                throw new IOException("未完成安装事务的恢复快照校验失败，请保留 DawnTrainer 目录后检查。");
        }
        return files;
    }

    private static void RollBack(string directory, string managed, string state)
    {
        TransactionFile[] files = ReadTransaction(directory, managed, state);
        // Inspect every target before changing any: never overwrite a third-party edit.
        foreach (TransactionFile file in files)
        {
            string hash = FileHash(file.Target);
            if (hash != file.Before && hash != file.After)
                throw new IOException("未完成安装事务中的文件已被其他程序修改，已停止自动恢复：" + Path.GetFileName(file.Target));
        }
        foreach (TransactionFile file in files)
        {
            if (FileHash(file.Target) != file.Before)
                SetFile(Path.Combine(directory, file.Name + ".before"), file.Target, file.Before);
            Step("rollback-" + file.Name);
        }
        // Rename is the commit marker for recovery too. Never delete live snapshots
        // in place: interruption during deletion would otherwise destroy the journal.
        FinishTransaction(directory, state);
    }

    private static void FinishTransaction(string directory, string state)
    {
        string completed = Path.Combine(state, ".completed-" + Guid.NewGuid().ToString("N"));
        Directory.Move(directory, completed);
        // Cleanup is optional after the atomic commit marker; its failure cannot undo
        // a successful operation or make an already-committed journal live again.
        try { DeleteTransactionDirectory(completed); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void ApplyTransaction(string managed, string state, string[] replacements, string expectedAssemblyHash, string expectedAssemblyAfterHash, string expectedPluginHash)
    {
        string directory = Path.Combine(state, ".transaction");
        string preparing = Path.Combine(state, ".preparing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(preparing);
        try
        {
            TransactionFile[] files = TransactionFiles(managed, state);
            for (int index = 0; index < files.Length; index++)
            {
                TransactionFile file = files[index];
                if (File.Exists(file.Target)) CopyDurable(file.Target, Path.Combine(preparing, file.Name + ".before"));
                file.Before = FileHash(Path.Combine(preparing, file.Name + ".before"));
                if (replacements[index] != null) CopyDurable(replacements[index], Path.Combine(preparing, file.Name + ".after"));
                file.After = FileHash(Path.Combine(preparing, file.Name + ".after"));
            }
            if (files[0].Before != expectedAssemblyHash) throw new IOException("游戏程序集在准备期间发生变化，已停止操作。");
            if (files[0].After != expectedAssemblyAfterHash) throw new IOException("程序集暂存文件在准备期间发生变化，已停止操作。");
            if (files[1].After != expectedPluginHash) throw new IOException("功能库在准备期间发生变化，已停止操作。");
            string journal = "DawnTrainer transaction 1\n" + String.Join("\n", files.Select(f => f.Name + "|" + f.Before + "|" + f.After).ToArray());
            WriteDurable(Path.Combine(preparing, "journal"), journal);
            // No target changes precede publication of the complete journal/snapshots.
            Directory.Move(preparing, directory);
            try
            {
                Step("transaction-published");
                foreach (TransactionFile file in files)
                {
                    if (FileHash(file.Target) != file.Before) throw new IOException("文件在安装期间发生变化：" + Path.GetFileName(file.Target));
                    SetFile(Path.Combine(directory, file.Name + ".after"), file.Target, file.After);
                    Step("after-" + file.Name);
                }
                if (files.Any(f => FileHash(f.Target) != f.After)) throw new IOException("文件在提交前发生变化，已保留恢复记录。");
                FinishTransaction(directory, state);
            }
            catch (Exception operationError)
            {
                try { RollBack(directory, managed, state); }
                catch (Exception recoveryError)
                { throw new IOException("操作失败且暂时无法恢复，已保留恢复记录；请退出占用文件的程序后重试。原错误：" + operationError.Message + "；恢复错误：" + recoveryError.Message, operationError); }
                throw new IOException("操作失败，已恢复操作前的游戏程序集、功能库和安装清单。原因：" + operationError.Message, operationError);
            }
        }
        finally
        {
            // A preparing directory was never published, so none of its contents are
            // needed for recovery. A published .transaction is deliberately retained.
            try { DeleteTransactionDirectory(preparing); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static int Main(string[] args) { return Run(args); }
    internal static int Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            string game = ResolveGamePath(args.FirstOrDefault(a => !a.StartsWith("--")), AppDomain.CurrentDomain.BaseDirectory);
            string managed = Path.Combine(game, @"DawnOfMan_Data\Managed");
            string assembly = Path.Combine(managed, "Assembly-CSharp.dll");
            string plugin = Path.Combine(managed, "DawnOfManTrainer.dll");
            string state = Path.Combine(game, "DawnTrainer");
            string backup = Path.Combine(state, "Assembly-CSharp.dll.original");
            string manifest = Path.Combine(state, "installed.sha256");
            bool uninstall = args.Contains("--uninstall");
            bool verify = args.Contains("--verify");
            string transaction = Path.Combine(state, ".transaction");
            string lockPath = Path.Combine(state, ".installer.lock");
            if (!File.Exists(assembly) && !Directory.Exists(transaction)) throw new Exception("找不到游戏程序集，请指定 DawnOfMan 游戏目录。");
#if INSTALLER_TESTS
            if (TestOriginalHash == null && System.Diagnostics.Process.GetProcessesByName("DawnOfMan").Length > 0) throw new Exception("请先退出 Dawn of Man，再安装或卸载。");
#else
            if (System.Diagnostics.Process.GetProcessesByName("DawnOfMan").Length > 0) throw new Exception("请先退出 Dawn of Man，再安装或卸载。");
#endif
            if (verify && Directory.Exists(transaction)) throw new IOException("检测到未完成的安装事务；校验模式不会修改文件，请正常运行安装器或卸载器以先恢复。");
            if (!verify) Directory.CreateDirectory(state);
            using (FileStream installLock = !verify || File.Exists(lockPath)
                ? new FileStream(lockPath, verify ? FileMode.Open : FileMode.OpenOrCreate, verify ? FileAccess.Read : FileAccess.ReadWrite, FileShare.None) : null)
            {
                if (!verify && Directory.Exists(transaction))
                {
                    RollBack(transaction, managed, state);
                    Console.WriteLine("已恢复上次未完成操作前的文件，继续执行本次操作。");
                    Step("recovered");
                }
                return Operate(managed, state, assembly, plugin, backup, manifest, uninstall, verify);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("操作停止：" + ex.Message); return 1; }
    }

    private static int Operate(string managed, string state, string assembly, string plugin, string backup, string manifest, bool uninstall, bool verify)
    {
            if (!File.Exists(assembly)) throw new Exception("找不到游戏程序集，请指定 DawnOfMan 游戏目录。");
            string currentHash = Hash(assembly);
            string backupHash = FileHash(backup);
            bool backupValid = backupHash != "-" && IsOriginal(backupHash);
            if (uninstall)
            {
                if (!backupValid) throw new Exception("未找到匹配的原文件备份，未执行卸载。");
                if (!IsOriginal(currentHash) && !Recorded(manifest, currentHash)) throw new Exception("游戏文件在安装后发生变化，已停止还原以保护当前文件。");
                if (!verify)
                    ApplyTransaction(managed, state, new[] { backup, null, null }, currentHash, backupHash, "-");
                Console.WriteLine(verify ? "卸载条件校验通过；未修改游戏文件。" : "卸载成功：游戏原文件已恢复，备份继续保留。"); return 0;
            }
            bool installed = !IsOriginal(currentHash);
            if (installed && (!Recorded(manifest, currentHash) || !backupValid))
                throw new Exception("程序集与适配的 1.8.3 / 1.8.4 原版不一致，未修改任何游戏文件。");
            if (!installed && File.Exists(backup) && !backupValid) throw new Exception("现有备份校验失败，请先保留并检查该备份。");
            if (!installed && backupValid && backupHash != currentHash)
                throw new Exception("现有原版备份属于另一游戏版本，请先保留并移走旧备份后重试；未覆盖该备份。");
            string bundle = AppDomain.CurrentDomain.BaseDirectory;
#if INSTALLER_TESTS
            if (TestBundle != null) bundle = TestBundle;
#endif
            string trainer = Path.Combine(bundle, "DawnOfManTrainer.dll");
            if (!File.Exists(trainer)) throw new Exception("安装包缺少 DawnOfManTrainer.dll。");
            string trainerHash = Hash(trainer);
            if (verify && installed && (!File.Exists(plugin) || Hash(plugin) != Hash(trainer))) throw new Exception("已安装功能库缺失或与此安装包不同，请退出游戏后重新运行安装器。");
            DefaultAssemblyResolver resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed); resolver.AddSearchDirectory(bundle);
            // Updates re-patch the verified original, so the game always matches this library's hooks.
            using (AssemblyDefinition gameAssembly = AssemblyDefinition.ReadAssembly(installed ? backup : assembly, new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
            using (AssemblyDefinition modAssembly = AssemblyDefinition.ReadAssembly(trainer, new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
            {
                Patch(gameAssembly.MainModule, modAssembly.MainModule);
                if (verify)
                {
                    Console.WriteLine((installed ? "安装状态、原文件备份和 " : "适配检查通过：1.8.3 / 1.8.4 原版，") + HookSummary(gameAssembly.MainModule) + " 校验通过；未修改游戏文件。");
                    return 0;
                }
                string staged = Path.Combine(state, ".assembly-" + Guid.NewGuid().ToString("N") + ".tmp");
                string stagedManifest = Path.Combine(state, ".manifest-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    gameAssembly.Write(staged);
                    using (AssemblyDefinition check = AssemblyDefinition.ReadAssembly(staged))
                    { if (!check.MainModule.AssemblyReferences.Any(a => a.Name == "DawnOfManTrainer")) throw new Exception("写入校验失败。"); }
                    string patchedHash = Hash(staged);
                    WriteDurable(stagedManifest, patchedHash);
                    if (!File.Exists(backup))
                    {
                        string candidate = Path.Combine(state, ".original-" + Guid.NewGuid().ToString("N") + ".tmp");
                        try
                        {
                            CopyDurable(assembly, candidate);
                            if (Hash(candidate) != currentHash || !IsOriginal(currentHash)) throw new IOException("原版备份校验失败，已停止操作。");
                            File.Move(candidate, backup); // Never replace an existing original backup.
                        }
                        finally { if (File.Exists(candidate)) File.Delete(candidate); }
                    }
                    if (FileHash(backup) != (backupValid ? backupHash : currentHash)) throw new IOException("原版备份在准备期间发生变化，已停止操作。");
                    ApplyTransaction(managed, state, new[] { staged, trainer, stagedManifest }, currentHash, patchedHash, trainerHash);
                }
                finally
                {
                    if (File.Exists(staged)) File.Delete(staged);
                    if (File.Exists(stagedManifest)) File.Delete(stagedManifest);
                }
                Console.WriteLine(installed ? "修改器已更新：已从原文件备份重新挂接，功能库已更新。" : "安装成功。启动游戏后按 F11 打开内置修改器。\n原文件备份：" + backup);
                return 0;
            }
    }
}
