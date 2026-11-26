const string Usage = """
Usage:
  MonoModTool apply <input> <output> <mod> [<mod> ...]
  MonoModTool empty <input> <output>
  MonoModTool check <input> <mod> <work-dir>
  MonoModTool inspect <assembly> <query> [<query> ...]

inspect queries: <type>, <type>::<member>, types:<text>, members:<type>, calls:<text>, diff:<other-assembly>
""";

if (args.Length == 0)
{
    Console.Error.Write(Usage);
    return 2;
}

return (args[0], args.Length) switch
{
    ("apply", >= 4) => Patching.Apply(args[1], args[2], args[3..]),
    ("empty", 3) => Patching.Apply(args[1], args[2], []),
    ("check", 4) => SwitchCheck.Run(args[1], args[2], args[3]),
    ("inspect", >= 3) => Inspect.Run(args[1], args[2..]),
    _ => Fail(),
};

int Fail()
{
    Console.Error.Write(Usage);
    return 2;
}
