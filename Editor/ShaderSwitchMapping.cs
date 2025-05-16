using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

// [Serializable]
// public class PropertyMapping
// {
//     public string oldName;
//     public string newName;
//     public ShaderUtil.ShaderPropertyType propType;
// }
//
// [Serializable]
// public class AppendProp
// {
//     public string name;
//     public ShaderUtil.ShaderPropertyType propType;
//     public Object value;
// }
//
// [CreateAssetMenu(menuName = "ShaderSwitcher/Mapping")]
// public class ShaderSwitchMapping : ScriptableObject
// {
//     public Shader oldShader;
//     public Shader newShader;
//     public List<PropertyMapping> mappings = new List<PropertyMapping>();
//     public List<AppendProp> appendProps = new List<AppendProp>();
//     public List<string> appendKeyword = new List<string>();
// }