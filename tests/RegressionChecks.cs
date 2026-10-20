using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using DawnTrainer;

internal static class RegressionChecks
{
    private static int passed, failed;
    private static void Check(bool condition, string name)
    {
        if (condition) { passed++; Console.WriteLine("PASS " + name); }
        else { failed++; Console.WriteLine("FAIL " + name); }
    }

    private static void PolicyChecks()
    {
        Check(TrainerPolicy.AllowsHotkeys(true, false, true, false, false, false), "normal map permits hotkeys");
        Check(!TrainerPolicy.AllowsHotkeys(false, false, true, false, false, false), "loading/title rejects hotkeys");
        Check(!TrainerPolicy.AllowsHotkeys(true, true, true, false, false, false), "trainer overlay rejects hotkeys");
        Check(!TrainerPolicy.AllowsHotkeys(true, false, false, false, false, false), "noninteractive camera rejects hotkeys");
        Check(!TrainerPolicy.AllowsHotkeys(true, false, true, true, false, false), "game menu real-pause rejects hotkeys");
        Check(!TrainerPolicy.AllowsHotkeys(true, false, true, false, true, false), "modal dialog rejects hotkeys even without pause");
        Check(!TrainerPolicy.AllowsHotkeys(true, false, true, false, false, true), "focused input rejects hotkeys even without modal");
        bool saveDialogAllowed = TrainerPolicy.AllowsHotkeys(true, false, true, true, true, true);
        Check(TrainerPolicy.SelectCommand(saveDialogAllowed, false, true, true, true, false, true, false) == TrainerCommand.None,
            "Delete in save-name dialog never dispatches KillSelection");
        Check(TrainerPolicy.SelectCommand(true, false, true, true, true, true, false, false) == TrainerCommand.CompleteSelection, "plain Insert builds");
        Check(TrainerPolicy.SelectCommand(true, false, true, true, true, false, true, false) == TrainerCommand.KillSelection, "plain Delete kills when explicitly enabled");
        Check(TrainerPolicy.SelectCommand(true, false, true, true, true, false, false, true) == TrainerCommand.GiveResource, "plain F10 gives resources");
        Check(TrainerPolicy.SelectCommand(true, false, true, true, true, false, false, false) == TrainerCommand.None, "unhandled key dispatches no trainer action");
        Check(TrainerPolicy.SelectCommand(true, false, false, false, false, true, true, true) == TrainerCommand.None, "disabled switches dispatch nothing");
        Check(TrainerPolicy.SelectCommand(true, true, true, true, true, true, true, true) == TrainerCommand.None, "modifier chords dispatch nothing");

        bool built = false;
        int completionCalls = 0, knownBuilds = 0, points = 0;
        Func<bool> complete = delegate { completionCalls++; built = true; return true; };
        Action record = delegate { knownBuilds++; if (knownBuilds == 1 || knownBuilds == 5) points += 2; };
        Check(TrainerPolicy.CompleteBuild(built, complete, record) && built && completionCalls == 1 && knownBuilds == 1 && points == 2,
            "successful first build records count and knowledge exactly once");
        Check(!TrainerPolicy.CompleteBuild(built, complete, record) && completionCalls == 1 && knownBuilds == 1 && points == 2,
            "repeated build skips completion and duplicate reward");
        built = false; knownBuilds = 4; points = 2;
        Check(TrainerPolicy.CompleteBuild(built, complete, record) && knownBuilds == 5 && points == 4,
            "successful threshold build records cumulative reward");
        int recordCalls = 0;
        Check(!TrainerPolicy.CompleteBuild(false, delegate { return false; }, delegate { recordCalls++; }) && recordCalls == 0,
            "unsuccessful completion records no build or reward");
    }

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition nested in type.NestedTypes)
            foreach (TypeDefinition item in AllTypes(nested)) yield return item;
    }
    private static IEnumerable<MethodReference> Calls(MethodDefinition method)
    {
        return method.Body.Instructions.Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference).Where(m => m != null);
    }
    private static bool Calls(MethodDefinition method, string type, string name)
    { return Calls(method).Any(m => m.DeclaringType.FullName == type && m.Name == name); }
    private static int Integer(Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Ldc_I4 || instruction.OpCode == OpCodes.Ldc_I4_S) return Convert.ToInt32(instruction.Operand);
        throw new InvalidDataException("Expected key-code constant, got " + instruction);
    }
    private static string Shape(MethodDefinition method)
    {
        return String.Join("|", method.Body.Instructions.Select(i =>
        {
            object operand = i.Operand;
            Instruction target = operand as Instruction;
            if (target != null) operand = "branch:" + method.Body.Instructions.IndexOf(target);
            return i.OpCode.Name + ":" + (operand == null ? "" : operand.ToString());
        }).ToArray());
    }
    private static void BinaryChecks(string pluginPath, string gamePath)
    {
        using (AssemblyDefinition plugin = AssemblyDefinition.ReadAssembly(pluginPath))
        using (AssemblyDefinition tests = AssemblyDefinition.ReadAssembly(typeof(RegressionChecks).Assembly.Location))
        using (AssemblyDefinition core = AssemblyDefinition.ReadAssembly(Path.Combine(gamePath, @"DawnOfMan_Data\Managed\UnityEngine.CoreModule.dll")))
        using (AssemblyDefinition installed = AssemblyDefinition.ReadAssembly(Path.Combine(gamePath, @"DawnOfMan_Data\Managed\Assembly-CSharp.dll")))
        {
            ModuleDefinition module = plugin.MainModule;
            TypeDefinition trainer = module.GetType("DawnTrainer.Trainer");
            MethodDefinition hotkeys = trainer.Methods.Single(m => m.Name == "HandleHotkeys");
            Check(Calls(hotkeys, "DawnTrainer.TrainerPolicy", "AllowsHotkeys")
                && Calls(hotkeys, "DawnOfMan.GameCamera", "isInteractive")
                && Calls(hotkeys, "DawnOfMan.GameCamera", "isTransitioning")
                && Calls(hotkeys, "DawnOfMan.TimeManager", "isRealPaused")
                && Calls(hotkeys, "MadrugaShared.GuiManager", "anyModalPanels")
                && Calls(hotkeys, "UnityEngine.GUIUtility", "get_keyboardControl"), "compiled runtime supplies all input-context guards");
            Check(Calls(trainer.Methods.Single(m => m.Name == "Tick"), "DawnTrainer.Trainer", "HandleHotkeys"), "compiled frame dispatch uses guarded handler");
            TypeDefinition keyCode = core.MainModule.GetType("UnityEngine.KeyCode");
            int f8 = Convert.ToInt32(keyCode.Fields.Single(f => f.Name == "F8").Constant);
            int f10 = Convert.ToInt32(keyCode.Fields.Single(f => f.Name == "F10").Constant);
            int insert = Convert.ToInt32(keyCode.Fields.Single(f => f.Name == "Insert").Constant);
            int delete = Convert.ToInt32(keyCode.Fields.Single(f => f.Name == "Delete").Constant);
            List<int> listenedKeys = new List<int>();
            for (int i = 1; i < hotkeys.Body.Instructions.Count; i++)
            {
                MethodReference call = hotkeys.Body.Instructions[i].Operand as MethodReference;
                if (call != null && call.DeclaringType.FullName == "UnityEngine.Input" && call.Name == "GetKeyDown")
                    listenedKeys.Add(Integer(hotkeys.Body.Instructions[i - 1]));
            }
            Check(Calls(hotkeys,"DawnTrainer.Trainer","ActionKeyDown") && listenedKeys.Count==0,
                "compiled action hotkeys read configured bindings through the guarded dispatcher");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="ActionKeyDown"),"DawnTrainer.Trainer","ReservedKeys"),"custom shortcuts recheck current game bindings at dispatch");
            Check(hotkeys.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == "inputBlockedUntilFrame")
                && Calls(hotkeys, "UnityEngine.Time", "get_frameCount"), "compiled hotkey handler consumes current game input frame");
            MethodDefinition selected = trainer.Methods.Single(m => m.Name == "CompleteSelected");
            Check(Calls(selected, "DawnTrainer.TrainerPolicy", "CompleteBuild"), "compiled quick-build uses tested completion policy");
            List<MethodReference> callbacks = AllTypes(trainer).SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(Calls).Where(m => m.DeclaringType.FullName == "DawnOfMan.KnowledgeManager" && m.Name == "onBuildableBuilt").ToList();
            Check(callbacks.Count == 1, "compiled quick-build contains game knowledge notification callback");
            TypeDefinition runtimePolicy = module.GetType("DawnTrainer.TrainerPolicy");
            TypeDefinition testedPolicy = tests.MainModule.GetType("DawnTrainer.TrainerPolicy");
            foreach (string name in new string[] { "AllowsHotkeys", "SelectCommand", "CompleteBuild" })
                Check(Shape(runtimePolicy.Methods.Single(m => m.Name == name)) == Shape(testedPolicy.Methods.Single(m => m.Name == name)),
                    "tested policy IL matches shipping DLL: " + name);
            string[] gates = { "DawnOfMan.GameGuiManager:updateInput", "DawnOfMan.GameGuiManager:updateSpeedInputs",
                "DawnOfMan.CameraModeInteractive:update", "DawnOfMan.CameraModeInteractive:fixedUpdate", "MadrugaShared.GuiManager:onEvent" };
            foreach (string gate in gates)
            {
                string[] parts = gate.Split(':');
                MethodDefinition method = installed.MainModule.GetType(parts[0]).Methods.Single(m => m.Name == parts[1]);
                Check(Calls(method, "DawnTrainer.Trainer", "BlockInput"), "installed input gate intact: " + gate);
            }
            Check((string)trainer.Fields.Single(f => f.Name == "Version").Constant == "1.4.1", "shipping version is 1.4.1");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="TameSelected"),"DawnOfMan.EntityManager","morphBeing")&&Calls(trainer.Methods.Single(m=>m.Name=="TameSelected"),"DawnOfMan.KnowledgeManager","onNewResidentAnimal"),"taming uses native species conversion and resident notification");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="CanTame"),"DawnOfMan.ContextAction","doable"),"taming preserves native capture eligibility checks");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="RepairSelected"),"DawnOfMan.Structure","increaseCondition")&&Calls(trainer.Methods.Single(m=>m.Name=="RepairSelected"),"DawnOfMan.Structure","resetRepairMaterials"),"building repair updates model and resets native repair materials");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="MissingMaterials"),"DawnOfMan.EntityAmounts","clone"),"material preview reads a cloned outstanding delivery list");
            Check(!Calls(trainer.Methods.Single(m=>m.Name=="SetSpeed"),"DawnOfMan.TimeManager","unpause")&&!Calls(trainer.Methods.Single(m=>m.Name=="SyncOverlayPause"),"DawnOfMan.TimeManager","unpause"),"slow-speed restoration never releases native menu pauses");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="TogglePause"),"DawnTrainer.FeaturePolicy","NextPauseSpeed")&&Calls(trainer.Methods.Single(m=>m.Name=="SetSpeed"),"DawnTrainer.OverlayPauseState","RequestSpeed"),"shipping speed controls use the tested manual and overlay pause paths");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="DrawInterfaceWindow"),"DawnTrainer.Trainer","ApplyUiModel")&&!Calls(trainer.Methods.Single(m=>m.Name=="OnGUI"),"DawnTrainer.Trainer","ApplyUiModel"),"native window callback commits navigation and text edits before next model refresh");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="UpdateUiModel"),"DawnTrainer.Trainer","WorldReady")&&Calls(trainer.Methods.Single(m=>m.Name=="UpdateFeatureModel"),"DawnTrainer.Trainer","WorldReady"),"UI model reads the world only after Tick adopted its EntityManager");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="OnGUI"),"DawnTrainer.TrainerView","RenderToast"),"hidden-panel hotkey results are shown as a toast");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="BlockInput"),"DawnTrainer.Trainer","PanelOwnsInput")&&Calls(trainer.Methods.Single(m=>m.Name=="Tick"),"DawnTrainer.Trainer","TrackPointer"),"open panel takes input only over itself, while typing or during its own drags");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="HandleHotkeys"),"DawnTrainer.Trainer","HotkeyKillConfirmed"),"kill hotkey asks for a second press when villagers are selected");
            Check(!Calls(trainer.Methods.Single(m=>m.Name=="OnGUI"),"UnityEngine.Matrix4x4","Scale")&&Calls(trainer.Methods.Single(m=>m.Name=="OnGUI"),"DawnTrainer.FeaturePolicy","PanelScale"),"panel text is drawn at screen resolution instead of scaled by GUI.matrix");
            Check(!Calls(trainer.Methods.Single(m=>m.Name=="Reset"),"DawnTrainer.Trainer","SetSpeed")&&Calls(trainer.Methods.Single(m=>m.Name=="Reset"),"DawnTrainer.OverlayPauseState","ResetSpeed")&&Calls(trainer.Methods.Single(m=>m.Name=="SyncOverlayPause"),"DawnTrainer.OverlayPauseState","ResumeReset"),"runtime reset uses deferred menu-safe speed restoration");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="Tick"),"DawnTrainer.Trainer","ObserveKillConfirmation")&&Calls(trainer.Methods.Single(m=>m.Name=="OnUiAction"),"DawnTrainer.Trainer","CancelKillConfirmation"),"kill confirmation observes selection changes every frame and cancels on navigation");
            foreach(string action in new[]{"CompleteSelected","KillSelected","SelectedVillagers","UpdateUiModel"})
                Check(Calls(trainer.Methods.Single(m=>m.Name==action),"DawnTrainer.Trainer","SelectionSnapshot"),"runtime action uses filtered selection snapshot: "+action);
            Check(Calls(trainer.Methods.Single(m=>m.Name=="CanComplete"),"DawnOfMan.Buildable","isBeingRecycled")&&Calls(trainer.Methods.Single(m=>m.Name=="Valid"),"DawnOfMan.EntityManager","isPendingDestruction"),"construction and selection reject recycling and pending destruction");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="OnGUI"),"DawnTrainer.FeaturePolicy","ClampPanelPosition"),"runtime window dragging uses bounded positions");
            TypeDefinition runtimeProduction = module.GetType("DawnTrainer.ProductionPolicy");
            TypeDefinition testedProduction = tests.MainModule.GetType("DawnTrainer.ProductionPolicy");
            foreach(string name in new[]{"ValidMultiplier","ScaledCapacity","ScaleBuildWork"})
                Check(Shape(runtimeProduction.Methods.Single(m=>m.Name==name))==Shape(testedProduction.Methods.Single(m=>m.Name==name)),"tested multiplier policy IL matches shipping DLL: "+name);
            foreach(string name in new[]{"getCapacity","isFull","getSpaceLeft","canStoreEntity"})
                Check(Calls(installed.MainModule.GetType("DawnOfMan.Slot").Methods.Single(m=>m.Name==name),"DawnTrainer.Trainer","PrepareSlot"),"installed slot capacity gate: "+name);
            Check(Calls(installed.MainModule.GetType("DawnOfMan.Slot").Methods.Single(m=>m.Name=="getSpaceLeft"),"DawnTrainer.Trainer","NonnegativeSpace"),"installed overfull slots never report negative free space");
            Check(Calls(installed.MainModule.GetType("DawnOfMan.BeingStateBuilding").Methods.Single(m=>m.Name=="increaseBuildingOperation"),"DawnTrainer.Trainer","ScaleBuildWork"),"installed construction scales native work before native completion");
            foreach(string type in new[]{"DawnOfMan.AiRuleGoGetResourceToStore","DawnOfMan.AiRuleTaskGetResource"})
                Check(Calls(installed.MainModule.GetType(type).Methods.Single(m=>m.Name=="run"),"DawnTrainer.Trainer","ResourceCarrySlots"),"installed ordinary resource planning scales carry capacity: "+type);
            Check(!Calls(installed.MainModule.GetType("DawnOfMan.PhysicalEntity").Methods.Single(m=>m.Name=="countEmptySlots"),"DawnTrainer.Trainer","CarryPlanSlots"),"general empty-slot queries keep their native meaning for bodies and equipment");
            Check(Calls(trainer.Methods.Single(m=>m.Name=="Reset"),"DawnTrainer.Trainer","ResetMultipliers")&&Calls(trainer.Methods.Single(m=>m.Name=="Tick"),"DawnTrainer.Trainer","ClearMultiplierWorld"),"reset and world transitions clear all production multipliers");
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            PolicyChecks();
            FeatureChecks.Run(Check);
            BinaryChecks(args[0], args[1]);
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL unexpected error: " + ex); }
        Console.WriteLine("RESULT passed=" + passed + " failed=" + failed + "; offline only, game not launched");
        return failed == 0 ? 0 : 1;
    }
}
