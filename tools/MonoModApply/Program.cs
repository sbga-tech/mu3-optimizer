using Mono.Cecil.Cil;
using MonoMod;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: MonoModApply <input> <output> <mod> [<mod> ...]");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var mods = args.Skip(2).Select(Path.GetFullPath).ToArray();

Directory.SetCurrentDirectory(Path.GetDirectoryName(input)!);
using var modder = new MonoModder
{
    InputPath = input,
    OutputPath = output,
    MissingDependencyThrow = false,
    GACPaths = Array.Empty<string>(),
};

modder.Read();
modder.MapDependencies();
foreach (var mod in mods)
{
    Console.WriteLine($"Loading mod {mod}");
    modder.ReadMod(mod);
}

modder.MapDependencies();
modder.AutoPatch();
modder.WriterParameters.SymbolWriterProvider = new PortablePdbWriterProvider();
modder.Write();
Console.WriteLine($"Patched {input} -> {output}");
return 0;
