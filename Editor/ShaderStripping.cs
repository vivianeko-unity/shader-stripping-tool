using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShaderStrippingTool
{
    /// <summary>
    ///     Strip shader variants that are included in the shader variant collection blacklist
    ///     Strip shader variants that are not included in the player log
    ///     Collect shader variants at build and add them to the shader variant collection blacklist
    /// </summary>
    public struct ShaderPass
    {
        public string name;
        public PassType type;
    }

    public class ShaderStripping : ScriptableObject, IPreprocessShaders
    {
        private const string ReportPath = "Assets/Shader Variants Stripping/report.txt";
        private readonly List<string> compiledKeywords;
        private readonly bool generateReport;
        private readonly string n = Environment.NewLine;
        private readonly List<PassType> passTypes;
        private readonly TextAsset playerLog;
        private readonly PreprocessShaders preprocessShaders;
        private readonly IShaderPassed[] shadersPassed;
        private readonly ShaderStrippingSettings shaderStrippingSettings;
        private readonly ShaderVariantCollection svcBlacklist;
        private readonly ShaderVariantCollection svcCompiled;
        private readonly ShaderVariantCollection svcWhitelist;
        private string report = "";

        public ShaderStripping()
        {
            shaderStrippingSettings = ShaderStrippingWindow.shaderStrippingSettings;
            playerLog = shaderStrippingSettings.PlayerLog;
            preprocessShaders = shaderStrippingSettings.PreprocessShaders;
            svcWhitelist = shaderStrippingSettings.SvcWhitelist;
            svcBlacklist = shaderStrippingSettings.SvcBlacklist;
            svcCompiled = shaderStrippingSettings.SvcCompiled;
            generateReport = shaderStrippingSettings.GenerateReport;
            passTypes = shaderStrippingSettings.PassTypes;
            compiledKeywords = shaderStrippingSettings.CompiledKeywords;
            shadersPassed = new IShaderPassed[] { new PlayerLogPassed(playerLog) };
        }

        public int callbackOrder { get; }

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> variants
        )
        {
            if (!shaderStrippingSettings || !playerLog) return;

            if (shader.name.Contains("Hidden"))
            {
                report += "internal shader: " + shader.name + n;
                return;
            }

            for (var i = variants.Count - 1; i >= 0; i--)
            {
                var stringKeywordsStrings = GetKeywordNames(variants[i].shaderKeywordSet);

                var variant = new ShaderVariantCollection.ShaderVariant
                {
                    shader = shader,
                    passType = snippet.passType,
                    keywords = stringKeywordsStrings
                };

                var pass = new ShaderPass { name = snippet.passName, type = snippet.passType };
                var isPassed = ShaderPassStripping(shader, pass, variant.keywords);


                switch (preprocessShaders)
                {
                    case PreprocessShaders.CollectVariants:
                        if (!passTypes.Contains(snippet.passType))
                            passTypes.Add(snippet.passType);

                        foreach (var key in variant.keywords)
                            if (!compiledKeywords.Contains(key))
                                compiledKeywords.Add(key);
                        svcCompiled.Add(variant);
                        _ = isPassed ? svcWhitelist.Add(variant) : svcBlacklist.Add(variant);
                        report += "collected shader: " + shader.name + ", pass: " + variant.passType
                                  + ", stage: " + snippet.shaderType
                                  + ", keywords: " + string.Join(" ", variant.keywords) + n;
                        break;

                    case PreprocessShaders.StripWithWhitelist when !svcWhitelist.Contains(variant):
                        variants.RemoveAt(i);
                        report += "stripped with whitelist: " + shader.name + ", pass: " + variant.passType
                                  + ", stage: " + snippet.shaderType
                                  + ", keywords: " + string.Join(" ", variant.keywords) + n;
                        break;

                    case PreprocessShaders.StripWithBlacklist when svcBlacklist.Contains(variant):
                        variants.RemoveAt(i);
                        report += "stripped with blacklist: " + shader.name + ", pass: " + variant.passType
                                  + ", stage: " + snippet.shaderType
                                  + ", keywords: " + string.Join(" ", variant.keywords) + n;
                        break;
                }

                if (generateReport)
                    File.WriteAllText(ReportPath, report);
            }
        }

        private static string[] GetKeywordNames(ShaderKeywordSet keywordSet)
        {
            var keywords = keywordSet.GetShaderKeywords();
            var result = new string[keywords.Length];
            for (var i = 0; i < keywords.Length; i++) result[i] = keywords[i].name;
            return result;
        }

        private bool ShaderPassStripping(Shader shader, in ShaderPass pass, string[] keywords)
        {
            foreach (var shaderPassed in shadersPassed)
                if (shaderPassed.IsPassed(shader, pass, keywords))
                    return true;
            return false;
        }
    }
}
