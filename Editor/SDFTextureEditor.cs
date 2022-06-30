﻿using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(SDFTexture))]
public class SDFTextureEditor : Editor
{
    enum Axis { X, Y, Z }

    static Mesh s_Quad;
    static SDFTexture s_SDFTexture;
    static Material s_Material;
    static MaterialPropertyBlock s_Props;
    static float s_Slice = 0.5f; // [0, 1]
    static Axis s_Axis = Axis.X;

    SerializedProperty m_SDF;
    SerializedProperty m_Size;
    SerializedProperty m_Resolution;

    static class Uniforms
    {
        internal static int _Z = Shader.PropertyToID("_Z");
        internal static int _Mode = Shader.PropertyToID("_Mode");
        internal static int _Axis = Shader.PropertyToID("_Axis");
        internal static int _DistanceScale = Shader.PropertyToID("_DistanceScale");
    }

    void OnEnable()
    {
        // Unity creates multiple Editors for a target.
        // Sharing all state in static variables is an iffy way around it.
        s_SDFTexture = target as SDFTexture;

        UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
        UnityEditor.SceneView.duringSceneGui += OnSceneGUI;

        m_SDF = serializedObject.FindProperty("m_SDF");
        m_Size = serializedObject.FindProperty("m_Size");
        m_Resolution = serializedObject.FindProperty("m_Resolution");
    }

    void OnDisable()
    {
        UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
    }

    static void DoBounds(SDFTexture sdftexture)
    {
        Handles.color = Color.white;
        Handles.DrawWireCube(Vector3.zero, sdftexture.voxelBounds.size);
    }

    static void DoHandles(Bounds bounds)
    {
        Vector3 dir = Vector3.forward;
        Vector3 perp = Vector3.right;
        switch (s_Axis)
        {
            case Axis.X: dir = Vector3.right; perp = Vector3.up; break;
            case Axis.Y: dir = Vector3.up; perp = Vector3.forward; break;
        }

        int axis = (int)s_Axis;
        Vector3[] offsets = {perp * bounds.extents[(axis + 1)%3], -perp * bounds.extents[(axis + 1)%3]};

        foreach(var offset in offsets)
        {
            Vector3 handlePos = dir * (s_Slice - 0.5f) * bounds.size[axis] + offset;
            float handleSize = new Vector2(bounds.size[(axis + 1)%3], bounds.size[(axis + 2)%3]).magnitude * 0.03f;
            handlePos = Handles.Slider(handlePos, dir, handleSize, Handles.CubeHandleCap, snap:-1);
            s_Slice = Mathf.Clamp01(handlePos[axis]/bounds.size[axis] + 0.5f);
        }
    }

    static void DoSDFSlice(Matrix4x4 matrix, Camera camera, Vector3Int voxelResolution, Bounds voxelBounds, float distanceScale, Texture sdf)
    {
        if (s_Quad == null)
            s_Quad = Resources.GetBuiltinResource(typeof(Mesh), "Quad.fbx") as Mesh;

        if (s_Material == null)
            s_Material = new Material(Shader.Find("Hidden/SDFTexture"));

        if (s_Props == null)
            s_Props = new MaterialPropertyBlock();

        s_Props.Clear();
        s_Props.SetFloat(Uniforms._Z, s_Slice);
        s_Props.SetInt(Uniforms._Axis, (int)s_Axis);
        s_Props.SetVector("_VoxelResolution", new Vector4(voxelResolution.x, voxelResolution.y, voxelResolution.z));
        s_Props.SetFloat(Uniforms._DistanceScale, distanceScale);
        s_Props.SetTexture("_SDF", sdf);

        Vector3 dir = Vector3.forward * voxelBounds.size.z;
        Quaternion rot = Quaternion.identity;
        switch (s_Axis)
        {
            case Axis.X: dir = Vector3.right * voxelBounds.size.x; rot = Quaternion.Euler(0, -90, 0); break;
            case Axis.Y: dir = Vector3.up * voxelBounds.size.y;  rot = Quaternion.Euler(90, 0, 0); break;
        }

        matrix *= Matrix4x4.Translate(dir * (s_Slice - 0.5f));
        matrix *= Matrix4x4.Scale(voxelBounds.size);
        matrix *= Matrix4x4.Rotate(rot);
        Graphics.DrawMesh(s_Quad, matrix, s_Material, layer:0, camera, submeshIndex:0, properties:s_Props);
    }

    static void OnSceneGUI(UnityEditor.SceneView sceneview)
    {
        if (s_SDFTexture == null)
            return;

        Bounds voxelBounds = s_SDFTexture.voxelBounds;
        var matrix = s_SDFTexture.transform.localToWorldMatrix;
        matrix *= Matrix4x4.Translate(voxelBounds.center);

        Handles.matrix = matrix;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

        DoBounds(s_SDFTexture);

        if (s_SDFTexture.sdf == null)
            return;

        if (voxelBounds.extents == Vector3.zero)
            return;

        DoSDFSlice(matrix, sceneview.camera, s_SDFTexture.voxelResolution, voxelBounds, distanceScale:1, s_SDFTexture.sdf);
        DoHandles(voxelBounds);
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("Texture", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_SDF);

        SDFTexture sdftexture = target as SDFTexture;

        switch(sdftexture.mode)
        {   
            case SDFTexture.Mode.None:
                EditorGUILayout.HelpBox("Assign a Texture3D with a static SDF or a 3D RenderTexture to write dynamic SDF to.", MessageType.Warning);
                break;
            case SDFTexture.Mode.Static:
                GUI.enabled = false;
                EditorGUILayout.Vector3IntField("Resolution", sdftexture.voxelResolution);
                GUI.enabled = true;
                break;
            case SDFTexture.Mode.Dynamic:
                EditorGUILayout.PropertyField(m_Size);

                Rect GetColumnRect(Rect totalRect, int column)
                {
                    Rect rect = totalRect;
                    rect.xMin += (totalRect.width - 8) * (column / 3f) + column * 4;
                    rect.width = (totalRect.width - 8) / 3f;
                    return rect;
                }
                Rect position = EditorGUILayout.GetControlRect();
                var label = EditorGUI.BeginProperty(position, new GUIContent("Resolution"), m_Resolution);
                position = EditorGUI.PrefixLabel(position, label);
                EditorGUIUtility.labelWidth = 13; // EditorGUI.kMiniLabelW
                EditorGUI.PropertyField(GetColumnRect(position, 0), m_Resolution, new GUIContent("X"));
                GUI.enabled = false;
                Vector3Int voxelRes = sdftexture.voxelResolution;
                EditorGUI.IntField(GetColumnRect(position, 1), "Y", voxelRes.y);
                EditorGUI.IntField(GetColumnRect(position, 2), "Z", voxelRes.z);
                GUI.enabled = true;
                EditorGUIUtility.labelWidth = 0;
                EditorGUI.EndProperty();
                break;
        }

        if (serializedObject.hasModifiedProperties)
            serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
        s_Axis = (Axis)EditorGUILayout.EnumPopup("Axis", s_Axis);

        float slice = EditorGUILayout.Slider("Slice", s_Slice, 0, 1);
        if (slice != s_Slice)
        {
            s_Slice = slice;
            SceneView.lastActiveSceneView?.Repaint();
        }
    }

    bool HasFrameBounds()
    {
        return true;
    }

    Bounds OnGetFrameBounds()
    {
        SDFTexture sdftexture = target as SDFTexture;
        Bounds bounds = sdftexture.voxelBounds;
        bounds.center += sdftexture.transform.position;
        bounds.size = Vector3.Scale(bounds.size, sdftexture.transform.lossyScale);
        return bounds;
    }
}