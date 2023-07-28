﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RT.CommandLine;
using RT.Util.ExtensionMethods;
using RT.Util.Text;

[assembly: AssemblyTitle("SouvenirPostBuildTool")]
[assembly: AssemblyDescription("Provides functionality to be run in a post-build step after compiling SouvenirLib.dll.")]
[assembly: AssemblyProduct("SouvenirPostBuildTool")]
[assembly: AssemblyCopyright("Copyright © Timwi 2021–2023")]
[assembly: Guid("e15e070f-0d34-4a7c-bce0-22af4567c1d1")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace SouvenirPostBuildTool
{
    class CommandLineOptions
    {
        [IsMandatory, IsPositional, Documentation("Specifies the full path and filename of SouvenirLib.dll.")]
        public string AssemblyPath = null;

        [IsMandatory, IsPositional, Documentation("Specifies the path to the KTANE files.")]
        public string GameFolder = null;

        [Option("-c", "--contributors"), Documentation("Specifies the path and filename to the CONTRIBUTORS.md file to be updated.")]
        public string ContributorsFile = null;

        [Option("-t", "--translations"), Documentation("If specified, the translation files in this folder are updated.")]
        public string TranslationsFolder = null;
    }

    public static class Program
    {
        static int Main(string[] args)
        {
            CommandLineOptions opt;
            try { opt = CommandLineParser.Parse<CommandLineOptions>(args); }
            catch (CommandLineParseException cpe)
            {
                cpe.WriteUsageInfoToConsole();
                return 1;
            }

            AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs e)
            {
                var assemblyPath = Path.Combine(opt.GameFolder, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
            };
            var assembly = Assembly.LoadFrom(opt.AssemblyPath);

            if (opt.ContributorsFile != null)
                DoContributorStuff(opt.ContributorsFile, assembly);

            if (opt.TranslationsFolder != null)
                DoTranslationStuff(opt.TranslationsFolder, assembly);

            return 0;
        }

        private static void DoContributorStuff(string filepath, Assembly assembly)
        {
            var moduleType = assembly.GetType("SouvenirModule");
            var module = Activator.CreateInstance(moduleType);
            var awakeMethod = moduleType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            awakeMethod.Invoke(module, null);
            var fldDictionary = moduleType.GetField("_moduleProcessors", BindingFlags.NonPublic | BindingFlags.Instance);
            var dictionary = (IDictionary) fldDictionary.GetValue(module);
            var contributorToModules = new Dictionary<string, List<string>>();
            foreach (DictionaryEntry entry in dictionary)
                contributorToModules.AddSafe((string) ((dynamic) entry.Value).Item3, (string) ((dynamic) entry.Value).Item2);

            const int numColumns = 5;

            var sb = new StringBuilder();
            sb.Append("# Souvenir implementors\n\nThe following is a list of modules supported by Souvenir, and the fine people who have contributed their effort to make it happen:\n\n\n");
            foreach (var group in contributorToModules.Where(gr => gr.Value.Count > numColumns).OrderByDescending(gr => gr.Value.Count).ThenBy(gr => gr.Key))
            {
                sb.Append($"## Implemented by {group.Key} ({group.Value.Count})\n\n");
                var tt = new TextTable { ColumnSpacing = 5, VerticalRules = true };
                var numItems = group.Value.Count;
                var numRows = (numItems + numColumns - 1) / numColumns;
                var col = 0;
                foreach (var column in group.Value.Order().Split(numRows))
                {
                    var row = 0;
                    foreach (var moduleName in column)
                        tt.SetCell(col, row++, moduleName);
                    col++;
                }
                sb.Append(tt.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(row => $"    {row.Trim().Replace("|", "│")}").JoinString("\n"));
                sb.Append("\n\n");
            }

            var remTable = new TextTable { ColumnSpacing = 5, RowSpacing = 1, VerticalRules = true, HorizontalRules = true, HeaderRows = 1 };
            remTable.SetCell(0, 0, "MODULE");
            remTable.SetCell(1, 0, "IMPLEMENTED BY");
            var remaining = contributorToModules
                .Where(gr => gr.Value.Count <= numColumns)
                .SelectMany(gr => gr.Value.Select(v => (author: gr.Key, module: v)))
                .OrderBy(tup => tup.module)
                .ToArray();
            for (var i = 0; i < remaining.Length; i++)
            {
                remTable.SetCell(0, i + 1, remaining[i].module);
                remTable.SetCell(1, i + 1, remaining[i].author);
            }
            sb.Append($"## Others\n\n{remTable.ToString().Split('\n').Select(r => r.Trim()).Where(row => !string.IsNullOrWhiteSpace(row) && !Regex.IsMatch(row, @"^-*\|-*$")).Select(row => $"    {row.Replace("|", "│").Replace("=│=", "═╪═").Replace("=", "═")}").JoinString("\n")}\n\n");

            File.WriteAllText(filepath, sb.ToString());
        }

        private static void DoTranslationStuff(string translationFilePath, Assembly assembly)
        {
            var questionsType = assembly.GetType("Souvenir.Question");
            var attributeType = assembly.GetType("Souvenir.SouvenirQuestionAttribute");

            var allInfos = new Dictionary<string, List<(FieldInfo fld, dynamic attr)>>();
            var addThe = new Dictionary<string, bool>();
            var trAnswers = new HashSet<string>();
            var trFArgs = new HashSet<string>();

            foreach (var fld in questionsType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                dynamic attr = fld.GetCustomAttribute(attributeType);
                var key = (string) attr.ModuleName;
                if (!allInfos.ContainsKey(key))
                    allInfos.Add(key, new List<(FieldInfo fld, dynamic attr)>());
                allInfos[key].Add((fld, attr));
                addThe[key] = attr.AddThe;
            }

            Console.WriteLine(string.Join("\r\n", trAnswers));
            Console.WriteLine("--------------------------");
            Console.WriteLine(string.Join("\r\n", trFArgs));

            foreach (var language in "de,eo,es,ja".Split(','))
            {
                var alreadyType = assembly.GetType($"Souvenir.Translation_{language}");
                var already = (IDictionary) (alreadyType == null ? null : (dynamic) Activator.CreateInstance(alreadyType))?.Translations;
                var sb = new StringBuilder();
                sb.AppendLine("        public override Dictionary<Question, TranslationInfo> Translations => new Dictionary<Question, TranslationInfo>");
                sb.AppendLine("        {");
                foreach (var kvp in allInfos)
                {
                    sb.AppendLine($"            // {(addThe[kvp.Key] ? "The " : "")}{kvp.Key}");
                    foreach (var (fld, attr) in kvp.Value)
                    {
                        var id = fld.GetValue(null);
                        var qText = (string) attr.QuestionText;
                        sb.AppendLine($"            // {qText}");
                        var exFormatArgs = new[] { (string) attr.ModuleNameWithThe };
                        if (attr.ExampleExtraFormatArguments != null)
                            exFormatArgs = exFormatArgs.Concat(((string[]) attr.ExampleExtraFormatArguments).Take((int) attr.ExampleExtraFormatArgumentGroupSize).Select(str => str == "\ufffdordinal" ? "first" : str)).ToArray();
                        try { sb.AppendLine($"            // {string.Format(qText, exFormatArgs)}"); }
                        catch { }
                        var answers = attr.AllAnswers == null || attr.AllAnswers.Length == 0 ? null : (string[]) attr.AllAnswers;
                        var formatArgs = attr.ExampleExtraFormatArguments == null || attr.ExampleExtraFormatArguments.Length == 0 ? null : ((string[]) attr.ExampleExtraFormatArguments).Distinct().ToArray();
                        dynamic ti = already?.Contains(id) == true ? already[id] : null;
                        sb.AppendLine($@"            [Question.{id}] = new TranslationInfo");
                        sb.AppendLine("            {");
                        sb.AppendLine($@"                QuestionText = ""{((string) (ti?.QuestionText) ?? qText).CLiteralEscape()}"",");
                        if (ti?.ModuleName != null)
                            sb.AppendLine($@"                ModuleName = ""{((string) ti.ModuleName).CLiteralEscape()}"",");
                        if (answers != null && attr.TranslateAnswers)
                        {
                            sb.AppendLine("                Answers = new Dictionary<string, string>");
                            sb.AppendLine("                {");
                            foreach (var answer in answers)
                                sb.AppendLine($@"                    [""{answer.CLiteralEscape()}""] = ""{(ti?.Answers?.ContainsKey(answer) == true ? (string) ti.Answers[answer] : answer).CLiteralEscape()}"",");
                            sb.AppendLine("                },");
                        }
                        if (formatArgs != null && attr.TranslateFormatArgs != null && Enumerable.Contains(attr.TranslateFormatArgs, true))
                        {
                            sb.AppendLine("                FormatArgs = new Dictionary<string, string>");
                            sb.AppendLine("                {");
                            for (var fArgIx = 0; fArgIx < formatArgs.Length; fArgIx++)
                                if (attr.TranslateFormatArgs[fArgIx % attr.ExampleExtraFormatArgumentGroupSize])
                                    sb.AppendLine($@"                    [""{formatArgs[fArgIx].CLiteralEscape()}""] = ""{(ti?.FormatArgs?.ContainsKey((string) formatArgs[fArgIx]) == true ? (string) ti.FormatArgs[(string) formatArgs[fArgIx]] : formatArgs[fArgIx]).CLiteralEscape()}"",");
                            sb.AppendLine("                },");
                        }
                        sb.AppendLine("            },");
                    }
                    sb.AppendLine("");
                }
                sb.AppendLine("        };");

                var path = Path.Combine(translationFilePath, $"Translation{language.ToUpperInvariant()}.cs");
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($@"File {path} does not exist.");
                    continue;
                }
                var alreadyFile = File.ReadLines(path).ToList();
                var p1 = alreadyFile.FindIndex(str => str.Trim() == "#region Translatable strings");
                var p2 = alreadyFile.FindIndex(p1 + 1, str => str.Trim() == "#endregion");
                if (p1 == -1 || p2 == -1)
                {
                    Console.Error.WriteLine($@"File {path} does not contain the “#region Translatable strings” and “#endregion” directives. Please put them back in.");
                    continue;
                }
                File.WriteAllText(path, $"{string.Join(Environment.NewLine, alreadyFile.Take(p1 + 1))}{Environment.NewLine}{sb}{string.Join(Environment.NewLine, alreadyFile.Skip(p2))}");
            }
        }

        /// <summary>
        ///     Escapes all characters in this string whose code is less than 32 or form invalid UTF-16 using C/C#-compatible
        ///     backslash escapes.</summary>
        static string CLiteralEscape(this string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var result = new StringBuilder(value.Length + value.Length / 2);

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\0': result.Append(@"\0"); break;
                    case '\a': result.Append(@"\a"); break;
                    case '\b': result.Append(@"\b"); break;
                    case '\t': result.Append(@"\t"); break;
                    case '\n': result.Append(@"\n"); break;
                    case '\v': result.Append(@"\v"); break;
                    case '\f': result.Append(@"\f"); break;
                    case '\r': result.Append(@"\r"); break;
                    case '\\': result.Append(@"\\"); break;
                    case '"': result.Append(@"\"""); break;
                    default:
                        if (c >= 0xD800 && c < 0xDC00)
                        {
                            if (i == value.Length - 1) // string ends on a broken surrogate pair
                                result.AppendFormat(@"\u{0:X4}", (int) c);
                            else
                            {
                                char c2 = value[i + 1];
                                if (c2 >= 0xDC00 && c2 <= 0xDFFF)
                                {
                                    // nothing wrong with this surrogate pair
                                    i++;
                                    result.Append(c);
                                    result.Append(c2);
                                }
                                else // first half of a surrogate pair is not followed by a second half
                                    result.AppendFormat(@"\u{0:X4}", (int) c);
                            }
                        }
                        else if (c >= 0xDC00 && c <= 0xDFFF) // the second half of a broken surrogate pair
                            result.AppendFormat(@"\u{0:X4}", (int) c);
                        else if (c >= ' ')
                            result.Append(c);
                        else // the character is in the 0..31 range
                            result.AppendFormat(@"\u{0:X4}", (int) c);
                        break;
                }
            }

            return result.ToString();
        }
    }
}