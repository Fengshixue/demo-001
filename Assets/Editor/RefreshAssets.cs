using UnityEditor;
using UnityEngine;

public class RefreshAssets : EditorWindow
{
    // 在 Unity 菜单栏添加一个选项
    [MenuItem("Tools/Refresh Assets")]
    public static void ShowWindow()
    {
        // 调用资源刷新方法
        AssetDatabase.Refresh();
        Debug.Log("资源数据库已刷新，正在导入未识别的资源...");
    }
}