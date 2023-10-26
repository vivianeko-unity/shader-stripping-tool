using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShaderStrippingTool
{
    public enum PreprocessShaders
    {
        CollectVariants,
        StripWithWhitelist,
        StripWithBlacklist
    }
    /// <summary>
    ///     Scriptable Object to handle all shader stripping settings
    ///     Show all collected pass types and keywords
    ///     Show detected global keywords and modify them
    /// </summary>
    public class ShaderStrippingSettings : ScriptableObject
    {
        private const string SvcBlacklistPath = "Assets/Shader Variants Stripping/SVC_Blacklist.shadervariants";
        private const string SvcWhitelistPath = "Assets/Shader Variants Stripping/SVC_Whitelist.shadervariants";
        private const string SvcCompiledPath = "Assets/Shader Variants Stripping/SVC_Compiled.shadervariants";

        [SerializeField] private PreprocessShaders preprocessShaders;
        [SerializeField] private ShaderVariantCollection svcBlacklist;
        [SerializeField] private ShaderVariantCollection svcWhitelist;
        [SerializeField] private ShaderVariantCollection svcCompiled;
        [SerializeField] private TextAsset playerLog;

        [SerializeField] private bool generateReport = true;
        [SerializeField] private List<PassType> passTypes;
        [SerializeField] private List<string> compiledKeywords;
        [SerializeField] private List<string> globalKeywords;

        public List<string> GlobalKeywords => globalKeywords;
        public PreprocessShaders PreprocessShaders => preprocessShaders;
        public ShaderVariantCollection SvcBlacklist => svcBlacklist;
        public ShaderVariantCollection SvcWhitelist => svcWhitelist;
        public ShaderVariantCollection SvcCompiled => svcCompiled;
        public TextAsset PlayerLog => playerLog;
        public bool GenerateReport => generateReport;
        public List<PassType> PassTypes => passTypes;
        public List<string> CompiledKeywords => compiledKeywords;
        public List<List<string>> GlobalKeywordsCombinations { get; private set; }

        private void Awake()
        {
            svcBlacklist =
                (ShaderVariantCollection)AssetDatabase.LoadAssetAtPath(SvcBlacklistPath,
                    typeof(ShaderVariantCollection));
            if (!svcBlacklist)
            {
                svcBlacklist = new ShaderVariantCollection();
                AssetDatabase.CreateAsset(svcBlacklist, SvcBlacklistPath);
                AssetDatabase.SaveAssets();
            }

            svcWhitelist =
                (ShaderVariantCollection)AssetDatabase.LoadAssetAtPath(SvcWhitelistPath,
                    typeof(ShaderVariantCollection));
            if (!svcWhitelist)
            {
                svcWhitelist = new ShaderVariantCollection();
                AssetDatabase.CreateAsset(svcWhitelist, SvcWhitelistPath);
                AssetDatabase.SaveAssets();
            }

            svcCompiled =
                (ShaderVariantCollection)AssetDatabase.LoadAssetAtPath(SvcCompiledPath,
                    typeof(ShaderVariantCollection));
            if (!svcCompiled)
            {
                svcCompiled = new ShaderVariantCollection();
                AssetDatabase.CreateAsset(svcCompiled, SvcCompiledPath);
                AssetDatabase.SaveAssets();
            }
        }

        private void OnValidate()
        {
            if (globalKeywords.Count > 0)
            {
                GlobalKeywordsCombinations = globalKeywords.GetAllCombinations();
                GlobalKeywordsCombinations.Add(new List<string>());
            }
            else
            {
                GlobalKeywordsCombinations = new List<List<string>> { new() };
            }

            Debug.Log("Global keywords updated");

            if (!playerLog) return;

            var playerLogPath = AssetDatabase.GetAssetPath(playerLog);
            var allLines = File.ReadAllLines(playerLogPath);
            var filteredLines = allLines.Where(x => x.Contains("Compiled shader: "));
            File.WriteAllLines(playerLogPath, filteredLines);

            try
            {
                PlayerLogParser.Parse(playerLog);
                Debug.Log("Player.log is valid");
            }
            catch (Exception e)
            {
                playerLog = null;
                Debug.LogException(e);
                throw;
            }
        }
    }
}