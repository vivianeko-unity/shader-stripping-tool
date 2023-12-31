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
        
        private readonly int[] analyzeTypes = { 1, 2, 3 };
        private readonly string[] analyzeTypesNames = { "Project", "Material", "Shader" };
        private bool analyze;
        private int analyzeType = 1;
        private int previousAnalyzeType;
        private bool fixAllWarnings;
        private bool warningsOnly;
        private bool strictMatchLocalKeywords = true;
        private bool strictMatchGlobalKeywords = true;
        
        private readonly List<List<string>> disabledKeywords = new();
        private readonly List<List<string>> enabledKeywords = new();
        private readonly List<bool> expandMaterials = new();
        private readonly List<Material> materials = new();
        private readonly List<List<ShaderVariantCollection.ShaderVariant>> variantsToRemove = new();
        private readonly List<List<ShaderVariantCollection.ShaderVariant>> variantsRemoved = new();
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
                if (!AssetDatabase.IsValidFolder("Assets/Shader Variants Stripping"))
                    AssetDatabase.CreateFolder("Assets", "Shader Variants Stripping");
                
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
            strictMatchLocalKeywords = GUILayout.Toggle(strictMatchLocalKeywords, "Strict local keywords match");
            strictMatchGlobalKeywords = GUILayout.Toggle(strictMatchGlobalKeywords, "Strict global keywords match");
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
                variantsRemoved.Clear();
            }

            switch (analyzeType)
            {
                case 1 when analyze:
                    FindProjectMaterials();
                    break;

                case 2:
                    selectedMaterial =
                        EditorGUILayout.ObjectField(selectedMaterial, typeof(Material), false) as Material;
                    if (analyze && selectedMaterial) materials.Add(selectedMaterial);
                    break;

                case 3:
                    selectedShader =
                        EditorGUILayout.ObjectField(selectedShader, typeof(Shader), false) as Shader;
                    if (analyze && selectedShader) FindSelectedShader();
                    break;
            }

            AnalyzeMaterials();
        }

        private void FindProjectMaterials()
        {
            Resources.LoadAll<Material>("");
            var allMaterials =  Resources.FindObjectsOfTypeAll<Material>();
            foreach (var mat in allMaterials)
            {
                if(!mat.shader.name.Contains("Hidden")) materials.Add(mat);
            }
        }

        private void FindSelectedShader()
        {
            Resources.LoadAll<Material>("");
            var allMaterials =  Resources.FindObjectsOfTypeAll<Material>();
            foreach (var mat in allMaterials)
            {
                if (mat!.shader == selectedShader) materials.Add(mat);
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
                    variantsRemoved.Add(new List<ShaderVariantCollection.ShaderVariant>());
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

                switch (localKeyword.isOverridable)
                {
                    case true when Shader.IsKeywordEnabled(keywordName)
                                   && !shaderStrippingSettings.GlobalKeywords.Contains(keywordName):
                        shaderStrippingSettings.GlobalKeywords.Add(keywordName);
                        break;
                    
                    case true when materials[index].IsKeywordEnabled(localKeyword)
                                   && !shaderStrippingSettings.GlobalKeywords.Contains(keywordName):
                        enabledKeywords[index].Add(keywordName);
                        break;
                    
                    case false when materials[index].IsKeywordEnabled(localKeyword):
                        enabledKeywords[index].Add(keywordName);
                        break;
                    
                    case false:
                        disabledKeywords[index].Add(keywordName);
                        break;
                }
            }
        }

        private List<string> FindCombinations(int index)
        {
            var localKeywordsCombinations = enabledKeywords[index].Count > 0
                ? enabledKeywords[index].GetAllCombinations()
                : new List<List<string>> { new() };

            var allCombinations = new List<string>();

            if (strictMatchGlobalKeywords)
            {
                if (strictMatchLocalKeywords)
                {
                    var result = new List<string>();
                    result.AddRange(shaderStrippingSettings.GlobalKeywords);
                    result.AddRange(enabledKeywords[index]);
                    var t = string.Join(" ", result);
                    if (!allCombinations.Contains(t)) allCombinations.Add(t);
                }
                else
                {
                    foreach (var local in localKeywordsCombinations)
                    {
                        var result = new List<string>();
                        result.AddRange(shaderStrippingSettings.GlobalKeywords);
                        result.AddRange(local);
                        var t = string.Join(" ", result);
                        if (!allCombinations.Contains(t)) allCombinations.Add(t);
                    }
                }
            }
            else
            {
                foreach (var global in shaderStrippingSettings.GlobalKeywordsCombinations)
                {
                    if (strictMatchLocalKeywords)
                    {
                        var result = new List<string>();
                        result.AddRange(global);
                        result.AddRange(enabledKeywords[index]);
                        var t = string.Join(" ", result);
                        if (!allCombinations.Contains(t)) allCombinations.Add(t);
                    }
                    else
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
                }
            }

            return allCombinations;
        }
        
        //check if blacklist svc contains variants with used keywords
        private void CheckSvc(int index)
        {
            var allCombinations = FindCombinations(index);

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
                    else if (shaderStrippingSettings.SvcCompiled.Contains(variantToRemove))
                        variantsRemoved[index].Add(variantToRemove);
                }
            }
        }

        private void UpdateGUI(int index, bool existInSvc)
        {
            var n = Environment.NewLine;
            var warning = new GUIStyle(EditorStyles.foldoutHeader)
            {
                normal = { textColor = Color.red },
                onNormal = {textColor = Color.red}
            };
            var blue = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Color.cyan },
                hover = { textColor = Color.cyan },
            };

            GUILayout.Space(10);

            GUILayout.BeginVertical("window");

            //show material and shader
            GUILayout.BeginHorizontal();
            _ = EditorGUILayout.ObjectField(materials[index], typeof(Material), false) as Material;
            _ = EditorGUILayout.ObjectField(materials[index].shader, typeof(Shader), false) as Shader;
            GUILayout.EndHorizontal();

            var removed = variantsRemoved[index];
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
                {
                    for (var i = 0; i < toRemove.Count; i++)
                        GUILayout.TextArea(toRemove[i].passType + " " + string.Join(" ", toRemove[i].keywords));
                }

            }

            GUILayout.BeginVertical("box");
            
            GUILayout.Label("vairants:", blue);
            for (var i = 0; i < removed.Count; i++)
                GUILayout.TextArea(removed[i].passType + " " + string.Join(" ", removed[i].keywords));
            
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