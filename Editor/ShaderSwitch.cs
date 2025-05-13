using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

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
            GUILayout.Label("→", GUILayout.Width(20));
            map.newName = EditorGUILayout.TextField(map.newName, GUILayout.MinWidth(120));

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

            // Suggestions
            if (!string.IsNullOrEmpty(map.oldName))
                ShowSuggestions(map.oldName, oldProps, s => map.oldName = s);
            if (!string.IsNullOrEmpty(map.newName))
                ShowSuggestions(map.newName, newProps, s => map.newName = s);

            EditorGUILayout.Space();
        }
        EditorGUILayout.EndScrollView();

        // Bottom buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Mapping", GUILayout.ExpandWidth(false)))
            mappings.Add(new PropertyMapping());

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save Mapping Asset", GUILayout.ExpandWidth(false)))
            SaveMappingAsset();

        if (GUILayout.Button("TestMat Switch Shader", GUILayout.ExpandWidth(false)))
            ApplyToMaterial(testMat,oldShader, newShader, mappings);
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
                ApplyToMaterial(mat, mapAsset.oldShader, mapAsset.newShader, mapAsset.mappings);
                EditorUtility.SetDirty(mat);
            }
        }
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Batch Apply", "Batch mapping applied to all materials.", "OK");
    }

    private void SwitchShaders(List<PropertyMapping> maps, Shader from, Shader to)
    {
        foreach (var mat in Selection.objects)
            if (mat is Material)
                ApplyToMaterial(mat as Material, from, to, maps);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Shader Switcher", "Shader 切换完成！", "OK");
    }

    private void ApplyToMaterial(Material mat, Shader from, Shader to, List<PropertyMapping> maps)
    {
        if (from != null && mat.shader != from) return;
        var cache = new List<(ShaderUtil.ShaderPropertyType, string, object)>();
        foreach (var m in maps)
        {
            if (m.propType == ShaderUtil.ShaderPropertyType.Color) cache.Add((m.propType, m.newName, mat.GetColor(m.oldName)));
            else if (m.propType == ShaderUtil.ShaderPropertyType.Float) cache.Add((m.propType, m.newName, mat.GetFloat(m.oldName)));
            else if (m.propType == ShaderUtil.ShaderPropertyType.Int) cache.Add((m.propType, m.newName, mat.GetInt(m.oldName)));
            else if (m.propType == ShaderUtil.ShaderPropertyType.Range) cache.Add((m.propType, m.newName, mat.GetFloat(m.oldName)));
            else if (m.propType == ShaderUtil.ShaderPropertyType.Vector)  cache.Add((m.propType, m.newName, mat.GetVector(m.oldName)));
            else if (m.propType == ShaderUtil.ShaderPropertyType.TexEnv) cache.Add((m.propType, m.newName, mat.GetTexture(m.oldName)));
        }
        mat.shader = to;
        foreach (var (t, name, value) in cache)
        {
            if (t == ShaderUtil.ShaderPropertyType.Color) mat.SetColor(name, (Color)value);
            else if (t == ShaderUtil.ShaderPropertyType.Float) mat.SetFloat(name, (float)value);
            else if (t == ShaderUtil.ShaderPropertyType.Int) mat.SetInt(name, (int)value);
            else if (t == ShaderUtil.ShaderPropertyType.Range) mat.SetFloat(name, (float)value);
            else if (t == ShaderUtil.ShaderPropertyType.Vector) mat.SetVector(name, (Vector4)value);
            else if (t == ShaderUtil.ShaderPropertyType.TexEnv) mat.SetTexture(name, (Texture)value);
        }
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

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
    }
}
