using System.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class SwitchCheck
{
    const string AnyPatchEnabled = "AnyPatchEnabled";
    static readonly TimeSpan ChildTimeout = TimeSpan.FromMinutes(3);
    const int ChildAttempts = 2;

    sealed record Switch(string Name, HashSet<string> Targets, bool ModuleWide);

    sealed record Arm(string Name, string[] Enabled);

    public static int Run(string inputPath, string modPath, string workPath)
    {
        var input = Path.GetFullPath(inputPath);
        var mod = Path.GetFullPath(modPath);
        var work = Path.GetFullPath(workPath);
        Directory.CreateDirectory(work);

        var (switches, anyTargets) = ReadSwitches(mod);
        var arms = new List<Arm> { new("all-off", []) };
        arms.AddRange(switches.Select(item => new Arm(item.Name, [item.Name])));
        arms.Add(new Arm("all-on", switches.Select(item => item.Name).ToArray()));

        var empty = Path.Combine(work, "empty.dll");
        var jobs = new List<(string Name, string[] Args, string? Ini)> { ("empty", ["empty", input, empty], null) };
        foreach (var arm in arms)
        {
            var ini = Path.Combine(work, arm.Name + ".ini");
            File.WriteAllText(ini, "[Optimization]\n" + string.Concat(switches.Select(item =>
                $"{item.Name}={(arm.Enabled.Contains(item.Name) ? 1 : 0)}\n")));
            jobs.Add((arm.Name, ["apply", input, Path.Combine(work, arm.Name + ".dll"), mod], ini));
        }

        var failed = new List<string>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, job =>
        {
            if (!RunSelf(job.Args, job.Ini, Path.Combine(work, job.Name + ".log")))
                lock (failed) failed.Add(job.Name);
        });
        foreach (var name in failed.Order(StringComparer.Ordinal))
            Console.WriteLine($"FAIL {name}: MonoMod failed; see {Path.Combine(work, name + ".log")}");
        if (failed.Count > 0)
            return 1;

        Dictionary<string, string> baseline;
        using (var module = ModuleDefinition.ReadModule(empty))
            baseline = TypeShapes.Fingerprints(module);

        var byName = switches.ToDictionary(item => item.Name);
        var violations = 0;
        foreach (var arm in arms)
        {
            var enabled = arm.Enabled.Select(name => byName[name]).ToArray();
            var allowed = new HashSet<string>(enabled.SelectMany(item => item.Targets));
            if (enabled.Length > 0)
                allowed.UnionWith(anyTargets);
            var moduleWide = enabled.Any(item => item.ModuleWide);

            var (changed, removed) = Compare(baseline, Path.Combine(work, arm.Name + ".dll"));
            var unexpected = moduleWide ? [] : changed.Where(name => !Covered(name, allowed)).ToArray();
            var problems = removed.Select(name => "removed " + name)
                .Concat(unexpected.Select(name => "unexpected change " + name))
                .ToList();
            if (enabled.Length == 1 && enabled[0].Targets.Count > 0 &&
                !changed.Any(name => Covered(name, enabled[0].Targets)))
                problems.Add("changed none of its patch targets");

            var scope = moduleWide ? "module-wide" : $"{allowed.Count} allowed";
            Console.WriteLine($"{(problems.Count == 0 ? "ok  " : "FAIL")} {arm.Name}: {changed.Count} changed, {scope}");
            foreach (var problem in problems)
                Console.WriteLine("       " + problem);
            violations += problems.Count;
        }
        return violations == 0 ? 0 : 1;
    }

    static (List<string> Changed, List<string> Removed) Compare(Dictionary<string, string> baseline, string candidatePath)
    {
        using var module = ModuleDefinition.ReadModule(candidatePath);
        var candidate = TypeShapes.Fingerprints(module);
        var changed = new SortedSet<string>(StringComparer.Ordinal);
        var removed = new List<string>();
        foreach (var (name, fingerprint) in baseline)
        {
            if (!candidate.TryGetValue(name, out var other))
                removed.Add(name);
            else if (other != fingerprint)
                changed.Add(name);
        }
        foreach (var name in candidate.Keys.Where(name => !baseline.ContainsKey(name)))
        {
            var owner = Ancestors(name).Skip(1).FirstOrDefault(baseline.ContainsKey);
            if (owner != null)
                changed.Add(owner);
        }
        removed.Sort(StringComparer.Ordinal);
        return (changed.ToList(), removed);
    }

    static bool Covered(string typeName, HashSet<string> targets) => Ancestors(typeName).Any(targets.Contains);

    static IEnumerable<string> Ancestors(string typeName)
    {
        for (var name = typeName; ; name = name[..name.LastIndexOf('/')])
        {
            yield return name;
            if (!name.Contains('/'))
                yield break;
        }
    }

    static (List<Switch> Switches, HashSet<string> AnyTargets) ReadSwitches(string modPath)
    {
        using var module = ModuleDefinition.ReadModule(modPath);
        var patchConfig = TypeShapes.Find(module, "MonoMod.PatchConfig")
            ?? throw new InvalidOperationException("MonoMod.PatchConfig not found in " + modPath);
        var names = patchConfig.Fields
            .Where(field => field.IsPublic && field.IsStatic && field.FieldType.FullName == "System.Boolean")
            .Select(field => field.Name)
            .ToArray();

        var targets = names.Append(AnyPatchEnabled).ToDictionary(name => name, _ => new HashSet<string>());
        void Add(IEnumerable<string> flags, string target)
        {
            foreach (var flag in flags)
            {
                if (!targets.TryGetValue(flag, out var set))
                    throw new InvalidOperationException($"MonoModIfFlag names unknown switch {flag} on {target}");
                set.Add(target);
            }
        }

        var ruleStrings = new HashSet<string>();
        foreach (var type in TypeShapes.All(module))
        {
            var typeFlags = TypeAncestors(type).SelectMany(Flags).ToArray();
            var patch = type.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType.FullName == "MonoMod.MonoModPatch");
            if (patch != null)
            {
                var target = ((string)patch.ConstructorArguments[0].Value).Replace("global::", "").Replace('+', '/');
                var memberFlags = type.Methods.SelectMany(method => Flags(method))
                    .Concat(type.Fields.SelectMany(field => Flags(field)))
                    .Concat(type.Properties.SelectMany(property => Flags(property)));
                Add(typeFlags.Concat(memberFlags).Distinct(), target);
            }

            foreach (var method in type.Methods)
            {
                if (method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName is
                        "MonoMod.OnStateEnterAttribute" or "MonoMod.OnStateLeaveAttribute"))
                    Add(typeFlags.Concat(Flags(method)).Distinct(), method.Parameters[0].ParameterType.FullName);

                if (TypeAncestors(type).Last().Namespace == "MonoMod" && method.HasBody)
                    ruleStrings.UnionWith(method.Body.Instructions
                        .Where(instruction => instruction.OpCode.Code == Code.Ldstr)
                        .Select(instruction => (string)instruction.Operand));
            }
        }

        var switches = names.Select(name => new Switch(name, targets[name], ruleStrings.Contains(name))).ToList();
        return (switches, targets[AnyPatchEnabled]);
    }

    static IEnumerable<TypeDefinition> TypeAncestors(TypeDefinition type)
    {
        for (var current = type; current != null; current = current.DeclaringType)
            yield return current;
    }

    static IEnumerable<string> Flags(ICustomAttributeProvider provider) => provider.CustomAttributes
        .Where(attribute => attribute.AttributeType.FullName == "MonoMod.MonoModIfFlag")
        .Select(attribute => (string)attribute.ConstructorArguments[0].Value);

    static bool RunSelf(string[] args, string? configPath, string logPath)
    {
        var host = Environment.ProcessPath!;
        var start = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (Path.GetFileNameWithoutExtension(host) == "dotnet")
            start.ArgumentList.Add(typeof(SwitchCheck).Assembly.Location);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        if (configPath != null)
            start.Environment["MU3_CONFIG_PATH"] = configPath;

        for (var attempt = 1; ; attempt++)
        {
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (process.WaitForExit(ChildTimeout))
            {
                File.WriteAllText(logPath, stdout.Result + stderr.Result);
                return process.ExitCode == 0;
            }
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            File.WriteAllText(logPath, stdout.Result + stderr.Result + $"\nattempt {attempt} killed after {ChildTimeout.TotalSeconds:0} s\n");
            if (attempt == ChildAttempts)
                return false;
        }
    }
}
