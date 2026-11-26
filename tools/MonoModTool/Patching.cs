using MonoMod;

static class Patching
{
    public static int Apply(string inputPath, string outputPath, string[] modPaths)
    {
        var input = Path.GetFullPath(inputPath);
        var output = Path.GetFullPath(outputPath);
        var mods = modPaths.Select(Path.GetFullPath).ToArray();

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
        if (mods.Length > 0)
            modder.WriterParameters.SymbolWriterProvider = new Mono.Cecil.Cil.PortablePdbWriterProvider();
        modder.Write();
        Console.WriteLine($"Patched {input} -> {output}");
        return 0;
    }
}
