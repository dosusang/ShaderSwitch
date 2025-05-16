using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.Rendering;

public class ShaderSwitcherWindow : EditorWindow
{
    private enum Tab { Manual, Batch }
    private Tab currentTab = Tab.Manual;

    // Manual tab fields
    private ShaderSwitchMapping mappingAsset;
    private Shader oldShader;
    private Shader newShader;
    private Shader cachedOldShader;
    private Shader cachedNewShader;
    private List<string> oldProps = new List<string>();
    private List<string> newProps = new List<string>();
    private List<PropertyMapping> mappings = new List<PropertyMapping>();
    private List<AppendProp> appendProps = new List<AppendProp>();
    private List<string> enableKeyword = new List<string>();
    private List<string> disableKeyword = new List<string>();
    
    private List<string> newKeywords = new List<string>();
    private MethodInfo getKeywordsMethod;
    
    private Vector2 scrollPos;
    private Material testMat;

    // Batch tab fields
    private List<ShaderSwitchMapping> batchMappings = new List<ShaderSwitchMapping>();
    private List<int> batchCounts = new List<int>();
    private Vector2 batchScroll;
    
    [MenuItem("Tools/Shader Switcher")]
    public static void ShowWindow()
    {
        var window = GetWindow<ShaderSwitcherWindow>("Shader Switcher");
        window.minSize = new Vector2(600, 450);
    }
    
    private void OnEnable()
    {
        // 通过反射获取内部方法 ShaderUtil.GetShaderGlobalKeywords
        var shaderUtilType = typeof(ShaderUtil);
        getKeywordsMethod = shaderUtilType.GetMethod(
            "GetShaderGlobalKeywords",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new Type[] { typeof(Shader) },
            null
        );
    }


    private void OnGUI()
    {
        // Tab bar
        currentTab = (Tab)GUILayout.Toolbar((int)currentTab, new string[] { "Manual", "Batch" });
        EditorGUILayout.Space();

        switch (currentTab)
        {
            case Tab.Manual:
                DrawManualTab();
                break;
            case Tab.Batch:
                DrawBatchTab();
                break;
        }
    }

    private void DrawManualTab()
    {
        EditorGUILayout.LabelField("Manual Mapping", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Load existing mapping asset
        EditorGUI.BeginChangeCheck();
        mappingAsset = (ShaderSwitchMapping)EditorGUILayout.ObjectField("Mapping Asset", mappingAsset, typeof(ShaderSwitchMapping), false);
        testMat =  (Material)EditorGUILayout.ObjectField("testMat", testMat, typeof(Material), false);
        if (EditorGUI.EndChangeCheck() && mappingAsset != null)
        {
            oldShader = mappingAsset.oldShader;
            newShader = mappingAsset.newShader;
            mappings = new List<PropertyMapping>(mappingAsset.mappings);
            appendProps = new List<AppendProp>(mappingAsset.appendProps);
            enableKeyword = new List<string>(mappingAsset.enableKeyword);
            disableKeyword = new List<string>(mappingAsset.disableKeyword);
            RefreshShaderProps();
        }

        // Shader selectors
        EditorGUI.BeginChangeCheck();
        oldShader = (Shader)EditorGUILayout.ObjectField("Old Shader", oldShader, typeof(Shader), false);
        newShader = (Shader)EditorGUILayout.ObjectField("New Shader", newShader, typeof(Shader), false);
        if (EditorGUI.EndChangeCheck())
            RefreshShaderProps();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Property / Keyword Mappings", EditorStyles.label);

        // Auto-stretching scroll area
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
        for (int i = 0; i < mappings.Count; i++)
        {
            var map = mappings[i];
            EditorGUILayout.BeginHorizontal("box");

            map.oldName = EditorGUILayout.TextField(map.oldName, GUILayout.MinWidth(120));
            if (!string.IsNullOrEmpty(map.oldName))
                ShowSuggestions(map.oldName, oldProps, s => map.oldName = s);
            GUILayout.Label("→", GUILayout.Width(20));
            map.newName = EditorGUILayout.TextField(map.newName, GUILayout.MinWidth(120));
            if (!string.IsNullOrEmpty(map.newName))
                ShowSuggestions(map.newName, newProps, s => map.newName = s);

            // Remove button
            if (GUILayout.Button("✕", GUILayout.Width(20)))
            {
                mappings.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }

            // Status info with color
            string status = GetMappingStatus(map);
            Color prevColor = GUI.color;
            GUI.color = status.Contains("OK") ? Color.green : Color.red;
            EditorGUILayout.LabelField(status, GUILayout.Width(120));
            GUI.color = prevColor;
            
            if(status.Contains("OK"))
                map.propType = GetPropertyType(oldShader, map.oldName);

            EditorGUILayout.EndHorizontal();
        }
        
        if (GUILayout.Button("Add Mapping"))
            mappings.Add(new PropertyMapping());
        
        // 新增：Append Props 编辑
        EditorGUILayout.Space(10);        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Append Properties", EditorStyles.boldLabel);
        for (int i = 0; i < appendProps.Count; i++)
        {
            var ap = appendProps[i];
            EditorGUILayout.BeginHorizontal("box");
            ap.name = EditorGUILayout.TextField("Name", ap.name);
            
            // 模糊匹配建议
            if (!string.IsNullOrEmpty(ap.name))
                ShowSuggestions(ap.name, newProps, s => ap.name = s);
            
            var exist = newProps.Contains(ap.name);
            string status = exist ? "OK" : "INVALID";
            Color prevColor = GUI.color;
            GUI.color = status.Contains("OK") ? Color.green : Color.red;
            EditorGUILayout.LabelField(status, GUILayout.Width(120));
            GUI.color = prevColor;
            
            if (exist)
            {
                ap.propType = GetPropertyType(newShader, ap.name);

                // 根据类型显示不同的值输入
                switch (ap.propType)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        ap.colorValue = EditorGUILayout.ColorField("Color Value", ap.colorValue);
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        ap.floatValue = EditorGUILayout.FloatField("Float Value", ap.floatValue);
                        break;
                    case ShaderUtil.ShaderPropertyType.Int:
                        ap.floatValue = EditorGUILayout.IntField("Int Value", (int)ap.floatValue);
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        ap.vectorValue = EditorGUILayout.Vector4Field("Vector Value", ap.vectorValue);
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        ap.textureValue = (Texture)
                            EditorGUILayout.ObjectField("Texture Value", ap.textureValue, typeof(Texture), false);
                        break;
                }
            }
            
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                appendProps.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }
        if (GUILayout.Button("Add Append Property"))
        {
            appendProps.Add(new AppendProp());
        }

       
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Enable Keywords", EditorStyles.boldLabel);
        for (int i = 0; i < enableKeyword.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            var temp = enableKeyword[i];
            temp = EditorGUILayout.TextField(temp);
            if (!string.IsNullOrEmpty(temp))
                ShowKeywordSuggestions(temp, s => temp = s);
            enableKeyword[i] = temp;

            string status = newKeywords.Contains(temp) ? "OK" : "INVALID";
            Color prevColor = GUI.color;
            GUI.color = status.Contains("OK") ? Color.green : Color.red;
            EditorGUILayout.LabelField(status, GUILayout.Width(120));
            GUI.color = prevColor;
            
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                enableKeyword.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("Add Keyword"))
        {
            enableKeyword.Add(string.Empty);
        }
        
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Disable Keywords", EditorStyles.boldLabel);
        for (int i = 0; i < disableKeyword.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            var temp = disableKeyword[i];
            temp = EditorGUILayout.TextField(temp);
            if (!string.IsNullOrEmpty(temp))
                ShowKeywordSuggestions(temp, s => temp = s);
            disableKeyword[i] = temp;

            string status = newKeywords.Contains(temp) ? "OK" : "INVALID";
            Color prevColor = GUI.color;
            GUI.color = status.Contains("OK") ? Color.green : Color.red;
            EditorGUILayout.LabelField(status, GUILayout.Width(120));
            GUI.color = prevColor;
            
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                disableKeyword.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("Add Keyword"))
        {
            disableKeyword.Add(string.Empty);
        }
        
        EditorGUILayout.EndScrollView();

        // Bottom buttons
        EditorGUILayout.BeginHorizontal();

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save Mapping Asset", GUILayout.ExpandWidth(false)))
            SaveMappingAsset();

        if (GUILayout.Button("TestMat Switch Shader", GUILayout.ExpandWidth(false)))
        {
            SaveMappingAsset();
            ApplyToMaterial(testMat, mappingAsset);
        }
        EditorGUILayout.EndHorizontal();
    }

 private void DrawBatchTab()
    {
        EditorGUILayout.LabelField("Batch Apply Multiple Mappings", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        batchScroll = EditorGUILayout.BeginScrollView(batchScroll, GUILayout.Height(150));
        for (int i = 0; i < batchMappings.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            batchMappings[i] = (ShaderSwitchMapping)EditorGUILayout.ObjectField(batchMappings[i], typeof(ShaderSwitchMapping), false);
            if (GUILayout.Button("✕", GUILayout.Width(20)))
            {
                batchMappings.RemoveAt(i);
                batchCounts.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("Add Mapping Asset", GUILayout.ExpandWidth(false)))
        {
            batchMappings.Add(null);
            batchCounts.Add(-1);
        }

        if (batchMappings.Count > 0)
        {
            if (GUILayout.Button("Refresh Counts", GUILayout.ExpandWidth(false)))
            {
                for (int i = 0; i < batchMappings.Count; i++)
                {
                    var mapAsset = batchMappings[i];
                    batchCounts[i] = mapAsset != null ? CountMaterialsUsing(mapAsset.oldShader) : -1;
                }
            }

            for (int i = 0; i < batchMappings.Count; i++)
            {
                var mapAsset = batchMappings[i];
                if (mapAsset != null && batchCounts[i] >= 0)
                    EditorGUILayout.LabelField($"{mapAsset.name}: {batchCounts[i]} materials");
            }

            if (GUILayout.Button("Apply All Mappings to All Materials", GUILayout.Height(30)))
            {
                foreach (var mapAsset in batchMappings)
                {
                    if (mapAsset != null)
                        ApplyBatch(mapAsset);
                }
            }
        }
    }
    private int CountMaterialsUsing(Shader shader)
    {
        if (shader == null) return 0;
        string[] guids = AssetDatabase.FindAssets("t:Material");
        int count = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && mat.shader == shader)
                count++;
        }
        return count;
    }

    private void ApplyBatch(ShaderSwitchMapping mapAsset)
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && mat.shader == mapAsset.oldShader)
            {
                ApplyToMaterial(mat, mapAsset);
                EditorUtility.SetDirty(mat);
            }
        }
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Batch Apply", "Batch mapping applied to all materials.", "OK");
    }

private void ApplyToMaterial(Material mat, ShaderSwitchMapping assets)
{
    Shader from = assets.oldShader;
    Shader to = assets.newShader;
    var maps = assets.mappings;
    var appendProps = assets.appendProps;
    var enableKeyword = assets.enableKeyword;
    var disableKeyword = assets.disableKeyword;
    
    if (from != null && mat.shader != from) return;

    // 缓存旧值
    var cache = new List<(ShaderUtil.ShaderPropertyType, string, object)>();
    foreach (var m in maps)
    {
        switch (m.propType)
        {
            case ShaderUtil.ShaderPropertyType.Color:
                cache.Add((m.propType, m.newName, mat.GetColor(m.oldName)));
                break;
            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                cache.Add((m.propType, m.newName, mat.GetFloat(m.oldName)));
                break;
            case ShaderUtil.ShaderPropertyType.Int:
                cache.Add((m.propType, m.newName, mat.GetInt(m.oldName)));
                break;
            case ShaderUtil.ShaderPropertyType.Vector:
                cache.Add((m.propType, m.newName, mat.GetVector(m.oldName)));
                break;
            case ShaderUtil.ShaderPropertyType.TexEnv:
                cache.Add((m.propType, m.newName, mat.GetTexture(m.oldName)));
                break;
        }
    }

    // 切 Shader
    mat.shader = to;

    // 还原映射值
    foreach (var (t, name, value) in cache)
    {
        switch (t)
        {
            case ShaderUtil.ShaderPropertyType.Color:
                mat.SetColor(name, (Color)value);
                break;
            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                mat.SetFloat(name, (float)value);
                break;
            case ShaderUtil.ShaderPropertyType.Int:
                mat.SetInt(name, (int)value);
                break;
            case ShaderUtil.ShaderPropertyType.Vector:
                mat.SetVector(name, (Vector4)value);
                break;
            case ShaderUtil.ShaderPropertyType.TexEnv:
                mat.SetTexture(name, (Texture)value);
                break;
        }
    }

    // 3. 附加写入额外属性值
    foreach (var ap in appendProps)
    {
        switch (ap.propType)
        {
            case ShaderUtil.ShaderPropertyType.Color:
                mat.SetColor(ap.name, ap.colorValue);
                break;
            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                mat.SetFloat(ap.name, ap.floatValue);
                break;
            case ShaderUtil.ShaderPropertyType.Int:
                mat.SetInt(ap.name, (int)ap.floatValue);
                break;
            case ShaderUtil.ShaderPropertyType.Vector:
                mat.SetVector(ap.name, ap.vectorValue);
                break;
            case ShaderUtil.ShaderPropertyType.TexEnv:
                mat.SetTexture(ap.name, ap.textureValue);
                break;
        }
    }

    // 4. 启用关键字
    foreach (var kw in enableKeyword)
        mat.EnableKeyword(kw);
    
    // 5. 关闭关键字
    foreach (var kw in disableKeyword)
        mat.DisableKeyword(kw);
}

    private void RefreshShaderProps()
    {
        if (oldShader != cachedOldShader)
        {
            oldProps = GetShaderProperties(oldShader);
            cachedOldShader = oldShader;
        }
        if (newShader != cachedNewShader)
        {
            newProps = GetShaderProperties(newShader);
            cachedNewShader = newShader;
            
            newProps = GetShaderProperties(newShader);
            cachedNewShader = newShader;

            // 利用反射获取关键词
            newKeywords.Clear();
            if (getKeywordsMethod != null && newShader != null)
            {
                var result = getKeywordsMethod.Invoke(null, new object[] { newShader }) as string[];
                if (result != null)
                    newKeywords.AddRange(result);
            }
        }
    }

    private List<string> GetShaderProperties(Shader shader)
    {
        var list = new List<string>();
        if (shader == null) return list;
        int count = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < count; i++)
            list.Add(ShaderUtil.GetPropertyName(shader, i));
        return list;
    }

    private string GetMappingStatus(PropertyMapping map)
    {
        bool oldExists = oldShader != null && oldProps.Contains(map.oldName);
        bool newExists = newShader != null && newProps.Contains(map.newName);
        if (!oldExists) return "Old Missing";
        if (!newExists) return "New Missing";
        var oldType = GetPropertyType(oldShader, map.oldName);
        var newType = GetPropertyType(newShader, map.newName);
        return oldType == newType ? "Exists/Type OK" : "Exists/Type Mismatch";
    }

    private ShaderUtil.ShaderPropertyType GetPropertyType(Shader shader, string propName)
    {
        if (shader == null) return ShaderUtil.ShaderPropertyType.Float;
        int count = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < count; i++)
            if (ShaderUtil.GetPropertyName(shader, i) == propName)
                return ShaderUtil.GetPropertyType(shader, i);
        return ShaderUtil.ShaderPropertyType.Float;
    }

    private void ShowSuggestions(string input, List<string> props, Action<string> onSelect)
    {
        EditorGUILayout.BeginVertical();

        foreach (var p in props)
        {
            if (input == p)
            {
                EditorGUILayout.EndVertical();
                return;
            }
        }
        
        foreach (var prop in props)
        {
            if (string.Equals(prop, input, StringComparison.OrdinalIgnoreCase)) continue;
            if (prop.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (GUILayout.Button(prop, GUILayout.MaxWidth(300)))
                {
                    onSelect(prop);
                    GUI.FocusControl(null);
                }
            }
        }
        EditorGUILayout.EndVertical();
    }

    private void ShowKeywordSuggestions(string input, Action<string> onSelect)
    {
        EditorGUILayout.BeginVertical();

        foreach (var p in newKeywords)
        {
            if (input == p)
            {
                EditorGUILayout.EndVertical();
                return;
            }
        }
        foreach (var keyword in newKeywords)
        {
            if (string.Equals(keyword, input, StringComparison.OrdinalIgnoreCase)) continue;
            if (keyword.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (GUILayout.Button(keyword, GUILayout.MaxWidth(300)))
                {
                    onSelect(keyword);
                    GUI.FocusControl(null);
                }
            }
        }
        EditorGUILayout.EndVertical();
    }
    
    private void SaveMappingAsset()
    {
        if (oldShader == null || newShader == null)
        {
            EditorUtility.DisplayDialog("保存失败", "请先指定 Old Shader 和 New Shader。", "OK");
            return;
        }

        // 自动生成文件名，例如 "Mapping_Lit_to_URPLit.asset"
        string fileName = $"Mapping_{oldShader.name.Replace("/", "_")}_to_{newShader.name.Replace("/", "_")}.asset";
        string folderPath = "Assets/ShaderSwitchMappings"; // 可以改为你希望保存的位置
        string assetPath = $"{folderPath}/{fileName}";

        // 确保目录存在
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            System.IO.Directory.CreateDirectory(folderPath);
            AssetDatabase.Refresh();
        }

        ShaderSwitchMapping asset = AssetDatabase.LoadAssetAtPath<ShaderSwitchMapping>(assetPath);

        bool createNew = asset == null; 
        if (asset == null)
        {
            // 不存在则创建新的
            asset = ScriptableObject.CreateInstance<ShaderSwitchMapping>();
            AssetDatabase.CreateAsset(asset, assetPath);
            Debug.Log($"创建新 Shader 映射文件：{assetPath}");
        }
        else
        {
            Debug.Log($"更新已有 Shader 映射文件：{assetPath}");
        }

        // 更新内容
        asset.oldShader = oldShader;
        asset.newShader = newShader;
        asset.mappings = new List<PropertyMapping>(mappings);
        asset.appendProps = new List<AppendProp>(appendProps);
        asset.enableKeyword = new List<string>(enableKeyword);

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (createNew)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
        }
    }
}
