using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Rustgrid.Shared.Sim;
using Rustgrid.Tactics.Agents;

namespace Rustgrid.Tactics.Core.Tests
{
    /// <summary>Keeps the engine-free boundary mechanical: a rule without a check is a wish.</summary>
    public class ArchitectureTests
    {
        private static readonly Assembly[] EngineFree =
        {
            typeof(Rng).Assembly,
            typeof(Game).Assembly,
            typeof(GreedyAgent).Assembly,
        };

        [Test]
        public void EngineFreeAssembliesDoNotReferenceUnity()
        {
            var offenders = EngineFree
                .SelectMany(a => a.GetReferencedAssemblies().Select(r => $"{a.GetName().Name} -> {r.Name}"))
                .Where(s => s.Contains("-> UnityEngine") || s.Contains("-> UnityEditor"))
                .ToList();
            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void EngineFreeAssembliesHaveNoMutableStatics()
        {
            var offenders = new List<string>();
            foreach (var type in EngineFree.SelectMany(a => a.GetTypes()))
            {
                if (type.IsDefined(typeof(CompilerGeneratedAttribute), false) || type.Name.Contains('<')) continue;
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral || field.Name.Contains('<')) continue;
                    // A readonly array is still mutable shared state.
                    if (!field.IsInitOnly || field.FieldType.IsArray) offenders.Add($"{type.FullName}.{field.Name}");
                }
            }
            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void EngineFreeSourcesAvoidNondeterministicApis()
        {
            var banned = new Regex(
                @"UnityEngine|UnityEditor|System\.Random|new Random\(|\bDateTime\b|\bStopwatch\b|Environment\.TickCount|Guid\.NewGuid" +
                @"|Math\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Exp|Log|Log10|Pow|Sqrt)\(|\bMath[Ff]\.|\bfloat\b|\bdouble\b|\bdecimal\b" +
                @"|\basync\b|\bawait\b|\bTask\b|\bThread\b|Parallel\.|\bdynamic\b|BinaryFormatter|Debug\.Log|GetHashCode\(\)\s*[;)]");
            string root = Path.GetFullPath(Path.Combine(TestData.GameRoot, "..", ".."));
            var dirs = new[]
            {
                Path.Combine(root, "packages", "com.rustgrid.shared.sim", "Runtime"),
                Path.Combine(TestData.GameRoot, "unity", "Packages", "com.rustgrid.tactics.core", "Runtime"),
                Path.Combine(TestData.GameRoot, "unity", "Packages", "com.rustgrid.tactics.agents", "Runtime"),
            };
            var hits = new List<string>();
            foreach (var file in dirs.SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories)))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i].Split("//")[0];
                    if (banned.IsMatch(code)) hits.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
            Assert.That(hits, Is.Empty);
        }
    }
}
