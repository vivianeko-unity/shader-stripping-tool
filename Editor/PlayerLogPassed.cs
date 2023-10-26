using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ShaderStrippingTool
{
    public interface IShaderPassed
    {
        bool IsPassed(Shader shader, in ShaderPass pass, params string[] keywords);
    }

    public readonly struct ShaderInfo : IEquatable<ShaderInfo>
    {
        private const string Unnamed = "Unnamed";
        private static readonly Regex PassIndexRegex = new(@"pass [\d]*");

        private readonly string shaderName;
        private readonly string passName;
        private readonly HashSet<string> keywords;
        private readonly bool isUnnamedPass;

        public ShaderInfo(string shaderName, string passName, params string[] keywords)
        {
            this.shaderName = shaderName.ToLower();
            this.passName = passName.ToLower();
            this.keywords = GenerateKeywordsSet(keywords);
            isUnnamedPass = IsUnnamedPass(this.passName);
        }

        public static bool CustomCompare(ShaderInfo a, ShaderInfo b)
        {
            return CompareShaderNames(a, b) && CompareKeywords(a, b) && ComparePassNames(a, b);
        }

        private static bool IsUnnamedPass(string passName)
        {
            return string.IsNullOrEmpty(passName) || passName.Contains(Unnamed) || PassIndexRegex.IsMatch(passName);
        }

        private static HashSet<string> GenerateKeywordsSet(IEnumerable<string> keywords)
        {
            return new HashSet<string>(keywords.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.ToLower()));
        }

        private static bool ComparePassNames(ShaderInfo a, ShaderInfo b)
        {
            if (a.isUnnamedPass && b.isUnnamedPass) return true;

            return a.passName == b.passName;
        }

        private static bool CompareKeywords(ShaderInfo a, ShaderInfo b)
        {
            if (!a.keywords.SetEquals(b.keywords)) return false;

            return true;
        }

        private static bool CompareShaderNames(ShaderInfo a, ShaderInfo b)
        {
            if (a.shaderName != b.shaderName) return false;

            return true;
        }

        public bool Equals(ShaderInfo other)
        {
            return shaderName == other.shaderName && passName == other.passName && keywords.SetEquals(other.keywords);
        }

        public override bool Equals(object obj)
        {
            return obj is ShaderInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = shaderName != null ? shaderName.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (passName != null ? passName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (keywords != null ? string.Join("", keywords).GetHashCode() : 0);

                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"{shaderName} pass: {passName} keywords: {GetKeywordsString()}";
        }

        private string GetKeywordsString()
        {
            return keywords.Count > 0 ? string.Join(" ", keywords) : "no keywords";
        }
    }

    public static class PlayerLogParser
    {
        private const string LinePrefix = "Compiled shader: ";
        private const string NoKeywords = "no keywords";
        private static readonly string[] PartNames = { ", pass: ", ", stage: ", ", keywords " };

        public static Dictionary<string, HashSet<ShaderInfo>> Parse(TextAsset playerLog)
        {
            return !playerLog ? new Dictionary<string, HashSet<ShaderInfo>>() : Parse(playerLog.text);
        }

        public static Dictionary<string, HashSet<ShaderInfo>> Parse(string playerLog)
        {
            if (string.IsNullOrEmpty(playerLog)) return new Dictionary<string, HashSet<ShaderInfo>>();

            var result = new Dictionary<string, HashSet<ShaderInfo>>();

            using var reader = new StringReader(playerLog);
            var lineNumber = 0;
            while (ReadNextLine(reader, out var line, ref lineNumber))
                try
                {
                    if (!TryParseLine(line, out var shaderName, out var passName, out var stageName,
                            out var keywords)) continue;

                    var shaderInfoSet = FindOrCreateSetForShader(result, shaderName);
                    var shaderInfo = new ShaderInfo(shaderName, passName, keywords);
                    shaderInfoSet.Add(shaderInfo);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Can't parse line: {lineNumber}");
                    Debug.LogException(e);
                    throw;
                }

            return result;
        }

        private static HashSet<ShaderInfo> FindOrCreateSetForShader(Dictionary<string, HashSet<ShaderInfo>> result,
            string shaderName)
        {
            if (!result.TryGetValue(shaderName, out var compiledList))
                result.Add(shaderName, compiledList = new HashSet<ShaderInfo>());

            return compiledList;
        }

        private static bool ReadNextLine(TextReader reader, out string line, ref int lineNumber)
        {
            lineNumber++;
            line = reader.ReadLine();
            return line != null;
        }

        private static bool TryParseLine(string line, out string shaderName, out string passName, out string stageName,
            out string[] keywords)
        {
            if (!line.StartsWith(LinePrefix))
            {
                shaderName = string.Empty;
                passName = string.Empty;
                stageName = string.Empty;
                keywords = Array.Empty<string>();
                return false;
            }

            line = line.Replace(LinePrefix, "");

            var parts = line.Split(PartNames, StringSplitOptions.None);
            shaderName = parts[0];
            passName = parts[1];
            stageName = parts[2];
            keywords = SplitKeywords(parts[3]);
            return true;
        }

        private static string[] SplitKeywords(string keywordsString)
        {
            return keywordsString.Contains(NoKeywords) ? Array.Empty<string>() : keywordsString.Split(' ');
        }
    }

    public class PlayerLogPassed : IShaderPassed
    {
        private readonly Dictionary<string, HashSet<ShaderInfo>> compiledShaders;

        public PlayerLogPassed(TextAsset playerLog)
        {
            compiledShaders = PlayerLogParser.Parse(playerLog);
        }

        public bool IsPassed(Shader shader, in ShaderPass pass, params string[] keywords)
        {
            var shaderVariant = GetShaderVariantInfo(shader, pass.name, keywords);
            if (!compiledShaders.TryGetValue(shader.name, out var compiledShaderVariants)) return false;

            foreach (var compiledShaderVariant in compiledShaderVariants)
                if (ShaderInfo.CustomCompare(shaderVariant, compiledShaderVariant))
                    return true;

            return false;
        }

        private static ShaderInfo GetShaderVariantInfo(Shader shader, string passName, string[] keywords)
        {
            var shaderVariant = new ShaderInfo(shader.name, passName, keywords);
            return shaderVariant;
        }
    }
}
