using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Installer
{
    private const string OriginalHash = "381061C26BA34BA38C95B9F82FA17777BB9CEACA5F07B0BB5AD0A0A0FF46A170";
    private const string DefaultGame = @"D:\Program Files (x86)\Steam\steamapps\common\DawnOfMan";
    private static string Hash(string file)
    { using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }

    private static MethodDefinition Method(ModuleDefinition module, string type, string method)
    { return module.GetType(type).Methods.Single(m => m.Name == method); }

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

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            string game = args.FirstOrDefault(a => !a.StartsWith("--")) ?? DefaultGame;
            game = Path.GetFullPath(game);
            string managed = Path.Combine(game, @"DawnOfMan_Data\Managed");
            string assembly = Path.Combine(managed, "Assembly-CSharp.dll");
            string plugin = Path.Combine(managed, "DawnOfManTrainer.dll");
            string state = Path.Combine(game, "DawnTrainer");
            string backup = Path.Combine(state, "Assembly-CSharp.dll.original");
            string manifest = Path.Combine(state, "installed.sha256");
            bool uninstall = args.Contains("--uninstall");
            bool verify = args.Contains("--verify");
            if (!File.Exists(assembly)) throw new Exception("找不到游戏程序集，请指定 DawnOfMan 游戏目录。");
            if (System.Diagnostics.Process.GetProcessesByName("DawnOfMan").Length > 0) throw new Exception("请先退出 Dawn of Man，再安装或卸载。");
            string currentHash = Hash(assembly);
            if (uninstall)
            {
                if (!File.Exists(backup) || Hash(backup) != OriginalHash) throw new Exception("未找到匹配的原文件备份，未执行卸载。");
                if (currentHash != OriginalHash && (!File.Exists(manifest) || File.ReadAllText(manifest).Trim() != currentHash)) throw new Exception("游戏文件在安装后发生变化，已停止还原以保护当前文件。");
                if (!verify)
                {
                    File.Copy(backup, assembly, true);
                    if (File.Exists(plugin)) File.Delete(plugin);
                }
                Console.WriteLine(verify ? "卸载条件校验通过；未修改游戏文件。" : "卸载成功：游戏原文件已恢复，备份继续保留。"); return 0;
            }
            if (currentHash != OriginalHash)
            {
                if (!File.Exists(manifest) || File.ReadAllText(manifest).Trim() != currentHash || !File.Exists(backup) || Hash(backup) != OriginalHash)
                    throw new Exception("程序集与适配的 1.8.4 原版不一致，未修改任何游戏文件。");
                string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DawnOfManTrainer.dll");
                if (verify && (!File.Exists(plugin) || Hash(plugin) != Hash(source))) throw new Exception("已安装功能库缺失或与此安装包不同，请退出游戏后重新运行安装器。");
                if (!verify) File.Copy(source, plugin, true);
                Console.WriteLine(verify ? "安装状态和原文件备份校验通过。" : "修改器已安装，功能库已更新。"); return 0;
            }
            if (File.Exists(backup) && Hash(backup) != OriginalHash) throw new Exception("现有备份校验失败，请先保留并检查该备份。");
            string bundle = AppDomain.CurrentDomain.BaseDirectory;
            string trainer = Path.Combine(bundle, "DawnOfManTrainer.dll");
            if (!File.Exists(trainer)) throw new Exception("安装包缺少 DawnOfManTrainer.dll。");
            DefaultAssemblyResolver resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed); resolver.AddSearchDirectory(bundle);
            using (AssemblyDefinition gameAssembly = AssemblyDefinition.ReadAssembly(assembly, new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
            using (AssemblyDefinition modAssembly = AssemblyDefinition.ReadAssembly(trainer, new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
            {
                ModuleDefinition module = gameAssembly.MainModule;
                Func<string, MethodReference> hook = name => module.ImportReference(Method(modAssembly.MainModule, "DawnTrainer.Trainer", name));
                Prefix(Method(module, "DawnOfMan.GameBehaviour", "Start"), hook("Bootstrap"));
                Prefix(Method(module, "DawnOfMan.GameBehaviour", "Update"), hook("Frame"));
                Gate(Method(module, "DawnOfMan.GameGuiManager", "updateInput"), hook("BlockInput"));
                Gate(Method(module, "DawnOfMan.GameGuiManager", "updateSpeedInputs"), hook("BlockInput"));
                Gate(Method(module, "DawnOfMan.CameraModeInteractive", "update"), hook("BlockInput"));
                Gate(Method(module, "DawnOfMan.CameraModeInteractive", "fixedUpdate"), hook("BlockInput"));
                Gate(Method(module, "MadrugaShared.GuiManager", "onEvent"), hook("BlockInput"));
                MethodDefinition decay = Method(module, "DawnOfMan.Stat", "decay");
                ILProcessor il = decay.Body.GetILProcessor(); Instruction original = decay.Body.Instructions[0];
                il.InsertBefore(original, il.Create(OpCodes.Ldarg_0)); il.InsertBefore(original, il.Create(OpCodes.Call, hook("IsLocked")));
                il.InsertBefore(original, il.Create(OpCodes.Brfalse, original));
                il.InsertBefore(original, il.Create(OpCodes.Ldarg_0)); il.InsertBefore(original, il.Create(OpCodes.Ldc_R4, 1f));
                il.InsertBefore(original, il.Create(OpCodes.Call, Method(module, "DawnOfMan.Stat", "setValue")));
                il.InsertBefore(original, il.Create(OpCodes.Ldc_I4_0)); il.InsertBefore(original, il.Create(OpCodes.Ret));
                if (verify) { Console.WriteLine("适配检查通过：1.8.4 原版，8 个挂接点可用；未修改游戏文件。"); return 0; }
                Directory.CreateDirectory(state);
                if (!File.Exists(backup)) File.Copy(assembly, backup, false);
                string staged = Path.Combine(state, "Assembly-CSharp.dll.staged");
                gameAssembly.Write(staged);
                using (AssemblyDefinition check = AssemblyDefinition.ReadAssembly(staged))
                { if (!check.MainModule.AssemblyReferences.Any(a => a.Name == "DawnOfManTrainer")) throw new Exception("写入校验失败。"); }
                string patchedHash = Hash(staged);
                File.Copy(trainer, plugin, true);
                File.WriteAllText(manifest, patchedHash, Encoding.ASCII);
                File.Replace(staged, assembly, null);
                Console.WriteLine("安装成功。启动游戏后按 F11 打开内置修改器。\n原文件备份：" + backup);
                return 0;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("操作停止：" + ex.Message); return 1; }
    }
}
