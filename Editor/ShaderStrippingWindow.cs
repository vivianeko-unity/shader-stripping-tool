using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ShaderStrippingTool
{
    public class ShaderStrippingWindow : EditorWindow
    {
        private const string SettingsPath = "Assets/Shader Variants Stripping/Shader_Stripping_Settings Asset.asset";
        public static ShaderStrippingSettings shaderStrippingSettings;
        
        private readonly int[] analyzeTypes = { 1, 2, 3, 4 };
        private readonly string[] analyzeTypesNames = { "Project", "Runtime", "Material", "Shader" };
        private bool analyze;
        private int analyzeType = 1;
        private int previousAnalyzeType;
        private bool fixAllWarnings;
        private bool warningsOnly;
        
        private readonly List<List<string>> disabledKeywords = new();
        private readonly List<List<string>> enabledKeywords = new();
        private readonly List<bool> expandMaterials = new();
        private readonly List<Material> materials = new();
        private readonly List<List<ShaderVariantCollection.ShaderVariant>> variantsToRemove = new();
        private Material selectedMaterial;
        private Shader selectedShader;

        private Vector2 scroll;

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            shaderStrippingSettings = (ShaderStrippingSettings)EditorGUILayout.ObjectField(
                AssetDatabase.LoadAssetAtPath(SettingsPath, typeof(ShaderStrippingSettings)),
                typeof(ShaderStrippingSettings), false);

            if (GUILayout.Button("Create settings asset"))
            {
                shaderStrippingSettings = CreateInstance<ShaderStrippingSettings>();
                AssetDatabase.CreateAsset(shaderStrippingSettings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            GUILayout.EndHorizontal();

            var helpBox = new GUIStyle(EditorStyles.helpBox);

            var n = Environment.NewLine + "+";

            GUILayout.Label("+" + "Select collect variants and add a player log file in the settings asset" + n
                            + "To get a player log file enable graphics log, and build" + n
                            + "Build again with 'Collect Variants' selected to populate the SVCs" + n
                            + "select a stripping type, if blacklist is selected come back here to analyze the project's materials",
                helpBox);

            if (!shaderStrippingSettings || 
                shaderStrippingSettings.PreprocessShaders != PreprocessShaders.StripWithBlacklist ||
                !shaderStrippingSettings.SvcBlacklist || shaderStrippingSettings.SvcBlacklist.shaderCount == 0)
                return;

            GUILayout.Space(10);
            GUILayout.Label("+" + "Use 'the tools below to analyze and exclude variants in use" + n
                            + "Open desired scene to get the correct global keywords" + n
                            + "use the global keywords list in settings asset to add not detected global keywords",
                helpBox);
            GUILayout.Space(10);
            ExploreKeywords();
        }

        [MenuItem("Tools/Shader Stripping Settings")]
        private static void Init()
        {
            GetWindow<ShaderStrippingWindow>();
        }

        private void ExploreKeywords()
        {
            previousAnalyzeType = analyzeType;
            analyzeType = EditorGUILayout.IntPopup("Analyze type", analyzeType, analyzeTypesNames, analyzeTypes);

            GUILayout.BeginHorizontal();
            warningsOnly = GUILayout.Toggle(warningsOnly, "Show warnings only");
            fixAllWarnings = GUILayout.Button("Fix all warnings");
            GUILayout.EndHorizontal();

            analyze = GUILayout.Button("Analyze") || materials.Count == 0 || previousAnalyzeType != analyzeType;

            if (analyze)
            {
                materials.Clear();
                expandMaterials.Clear();
                enabledKeywords.Clear();
                disabledKeywords.Clear();
                variantsToRemove.Clear();
            }

            switch (analyzeType)
            {
                case 1 when analyze:
                    FindProjectMaterials();
                    break;

                case 2 when analyze:
                    FindRuntimeMaterials();
                    break;

                case 3:
                    selectedMaterial =
                        EditorGUILayout.ObjectField(selectedMaterial, typeof(Material), false) as Material;
                    if (analyze && selectedMaterial) materials.Add(selectedMaterial);
                    break;

                case 4:
                    selectedShader =
                        EditorGUILayout.ObjectField(selectedShader, typeof(Shader), false) as Shader;
                    if (analyze && selectedShader) FindSelectedShader();
                    break;
            }

            AnalyzeMaterials();
        }

        private void FindProjectMaterials()
        {
            var allMaterials = AssetDatabase.FindAssets("t:Material");
            for (var i = 0; i < allMaterials.Length; i++)
            {
                allMaterials[i] = AssetDatabase.GUIDToAssetPath(allMaterials[i]);
                var selected = AssetDatabase.LoadAssetAtPath(allMaterials[i], typeof(Material)) as Material;
                materials.Add(selected);
            }
        }

        private void FindRuntimeMaterials()
        {
            var sceneMaterials = FindObjectsOfType<Material>();
            materials.AddRange(sceneMaterials);
        }

        private void FindSelectedShader()
        {
            var allMaterials = AssetDatabase.FindAssets("t:Material");
            for (var i = 0; i < allMaterials.Length; i++)
            {
                allMaterials[i] = AssetDatabase.GUIDToAssetPath(allMaterials[i]);
                var selected = AssetDatabase.LoadAssetAtPath(allMaterials[i], typeof(Material)) as Material;
                if (selected!.shader == selectedShader) materials.Add(selected);
            }
        }

        private void AnalyzeMaterials()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (var i = 0; i < materials.Count; i++)
            {
                if(materials[i] == null) continue;
                if (analyze)
                {
                    var counter = i * 100f / materials.Count / 100f;
                    EditorUtility.DisplayProgressBar("Analyzing materials", materials[i].name, counter);
                    expandMaterials.Add(false);
                    enabledKeywords.Add(new List<string>());
                    disabledKeywords.Add(new List<string>());
                    variantsToRemove.Add(new List<ShaderVariantCollection.ShaderVariant>());
                    AnalyzeKeywords(i);
                    CheckSvc(i);
                }

                var existInSvc = variantsToRemove[i].Count > 0;
                switch (warningsOnly)
                {
                    case true when existInSvc:
                        UpdateGUI(i, true);
                        break;
                    case false:
                        UpdateGUI(i, existInSvc);
                        break;
                }
            }

            EditorUtility.ClearProgressBar();

            EditorGUILayout.EndScrollView();
        }

        //check what keywords are enabled locally and globally
        private void AnalyzeKeywords(int index)
        {
            var keywordSpace = materials[index].shader.keywordSpace;
            foreach (var localKeyword in keywordSpace.keywords)
            {
                if (!shaderStrippingSettings.CompiledKeywords.Contains(localKeyword.name)) continue;

                var keywordName = localKeyword.name;

                if (localKeyword.isOverridable)
                {
                    if (Shader.IsKeywordEnabled(keywordName)
                        && !shaderStrippingSettings.GlobalKeywords.Contains(keywordName))
                        shaderStrippingSettings.GlobalKeywords.Add(keywordName);
                }
                else
                {
                    if (materials[index].IsKeywordEnabled(localKeyword))
                        enabledKeywords[index].Add(keywordName);
                    else
                        disabledKeywords[index].Add(keywordName);
                }
            }
        }

        //check if blacklist svc contains variants with used keywords
        private void CheckSvc(int index)
        {
            var localKeywordsCombinations = enabledKeywords[index].Count > 0
                ? enabledKeywords[index].GetAllCombinations()
                : new List<List<string>> { new() };

            var allCombinations = new List<string>();

            foreach (var global in shaderStrippingSettings.GlobalKeywordsCombinations)
            {
                foreach (var local in localKeywordsCombinations)
                {
                    var result = new List<string>();
                    result.AddRange(global);
                    result.AddRange(local);
                    var t = string.Join(" ", result);
                    if (!allCombinations.Contains(t)) allCombinations.Add(t);
                }
            }

            foreach (var t in allCombinations)
            {
                var combinationKeywords = t.Split(" ");
                foreach (var pass in shaderStrippingSettings.PassTypes)
                {
                    var variantToRemove = new ShaderVariantCollection.ShaderVariant
                    {
                        shader = materials[index].shader,
                        keywords = combinationKeywords,
                        passType = pass
                    };
                    if (shaderStrippingSettings.SvcBlacklist.Contains(variantToRemove))
                        variantsToRemove[index].Add(variantToRemove);
                }
            }
        }

        private void UpdateGUI(int index, bool existInSvc)
        {
            var n = Environment.NewLine;
            var warning = new GUIStyle(EditorStyles.foldoutHeader) { normal = { textColor = Color.red } };
            var blue = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.cyan } };

            GUILayout.Space(10);

            GUILayout.BeginVertical("window");

            //show material and shader
            GUILayout.BeginHorizontal();
            _ = EditorGUILayout.ObjectField(materials[index], typeof(Material), false) as Material;
            _ = EditorGUILayout.ObjectField(materials[index].shader, typeof(Shader), false) as Shader;
            GUILayout.EndHorizontal();

            //show status in SVC
            if (existInSvc)
            {
                var toRemove = variantsToRemove[index];

                GUILayout.BeginHorizontal();

                expandMaterials[index] = EditorGUILayout.Foldout(expandMaterials[index], 
                    "Warning: variants exist in blacklist", true, warning);
                
                if (GUILayout.Button("Remove from blacklist", GUILayout.Width(200)) || fixAllWarnings)
                    for (var i = toRemove.Count - 1; i >= 0; i--)
                    {
                        shaderStrippingSettings.SvcBlacklist.Remove(toRemove[i]);
                        toRemove.Remove(toRemove[i]);
                    }
                GUILayout.EndHorizontal();

                if (expandMaterials[index])
                    for (var i = 0; i < toRemove.Count; i++)
                        GUILayout.TextArea(toRemove[i].passType + " " + string.Join(" ", toRemove[i].keywords));
            }

            GUILayout.BeginVertical("box");
            
            //show Keywords in use
            GUILayout.Label("Enabled local keywords:", blue);
            GUILayout.TextField(enabledKeywords[index].Count > 0
                ? string.Join(n, enabledKeywords[index])
                : "<no keywords>");

            //show Keywords not in use
            GUILayout.Label("Disabled local keywords:", blue);
            GUILayout.TextField(disabledKeywords[index].Count > 0
                ? string.Join(n, disabledKeywords[index])
                : "<no keywords>");

            GUILayout.EndVertical();

            GUILayout.EndVertical();
        }
    }
}