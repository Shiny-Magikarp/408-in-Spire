using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Spire408.AI;

// The game's mod loader does not consume this mod's NuGet .deps.json. Register
// its two managed inference dependencies before any ONNX method is JIT-compiled.
public static class WakuuRuntime
{
    public static void Initialize(string directory)
    {
        var context = AssemblyLoadContext.GetLoadContext(typeof(WakuuRuntime).Assembly)!;
        foreach (string name in new[] { "System.Numerics.Tensors", "Microsoft.ML.OnnxRuntime" })
        {
            if (context.Assemblies.Any(a => a.GetName().Name == name)) continue;
            string path = Path.Combine(directory, name + ".dll");
            if (!System.IO.File.Exists(path)) throw new FileNotFoundException("瓦库运行库缺失：" + path);
            context.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        var onnx = context.Assemblies.Single(a => a.GetName().Name == "Microsoft.ML.OnnxRuntime");
        string native = Path.Combine(directory, "onnxruntime.dll");
        if (!System.IO.File.Exists(native)) native = Path.Combine(directory, "runtimes", "win-x64", "native", "onnxruntime.dll");
        string library = native;
        try
        {
            NativeLibrary.SetDllImportResolver(onnx, (name, assembly, searchPath) =>
                name is "onnxruntime" or "onnxruntime.dll" ? NativeLibrary.Load(library) : IntPtr.Zero);
        }
        catch (InvalidOperationException)
        {
            // Another mod using this same ONNX assembly may already own its
            // resolver. Windows can share the identical installed runtime.
            if (System.IO.File.Exists(library)) NativeLibrary.Load(library);
        }
    }
}
