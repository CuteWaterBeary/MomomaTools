﻿#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MomomaAssets
{
    sealed class CatenaryGenerator : MonoBehaviour, ISerializationCallbackReceiver
    {
        enum Axis { X, Y, Z }

        [SerializeField]
        MeshRenderer m_SourceMeshRenderer = null;
        [SerializeField]
        Axis m_Axis = Axis.Z;
        [SerializeField]
        Transform[] m_Anchors = new Transform[0];

        [SerializeField, HideInInspector]
        GameObject[] m_MeshObjects = new GameObject[0];
        [SerializeField]
        float m_Catenary = 10f;
        [SerializeField]
        Gradient m_Gradient = new Gradient();

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void OnHierarchyChanged()
        {
            if (this == null)
            {
                DestroyAllCurves();
                EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            }
        }

        void OnReset()
        {
            hideFlags |= HideFlags.DontSaveInBuild;
        }

        void DestroyAllCurves()
        {
            foreach (var go in m_MeshObjects)
            {
                if (go == null || !go.scene.isLoaded)
                    continue;
                var oldMesh = go.GetComponent<MeshFilter>()?.sharedMesh;
                Undo.DestroyObjectImmediate(go);
                if (oldMesh != null)
                    Undo.DestroyObjectImmediate(oldMesh);
            }
            if (this != null && this.gameObject.scene.isLoaded)
            {
                using (var so = new SerializedObject(this))
                using (var sp = so.FindProperty(nameof(m_MeshObjects)))
                {
                    sp.ClearArray();
                    so.ApplyModifiedProperties();
                }
            }
        }

        void RecalculateMesh()
        {
            if (m_Anchors.Length < 2 || m_SourceMeshRenderer == null)
                return;
            var sourceMesh = m_SourceMeshRenderer.GetComponent<MeshFilter>().sharedMesh;
            if (sourceMesh == null)
                return;
            using (var so = new SerializedObject(this))
            using (var sp = so.FindProperty(nameof(m_MeshObjects)))
            {
                DestroyAllCurves();
                var bounds = sourceMesh.bounds;
                var srcVetices = sourceMesh.vertices;
                var srcNormals = sourceMesh.normals;
                var srcColors = sourceMesh.colors32;
                srcColors = srcColors != null && srcColors.Length == srcVetices.Length ? srcColors : new Color32[srcVetices.Length];
                var dstVertices = new Vector3[srcVetices.Length];
                var dstNormals = new Vector3[srcVetices.Length];
                var unitLength = 0f;
                switch (m_Axis)
                {
                    case Axis.X: unitLength = bounds.size.x; break;
                    case Axis.Y: unitLength = bounds.size.y; break;
                    case Axis.Z: unitLength = bounds.size.z; break;
                    default: throw new System.ArgumentOutOfRangeException(nameof(m_Axis));
                }
                sp.ClearArray();
                for (var i = 0; i < m_Anchors.Length - 1; ++i)
                {
                    if (m_Anchors[i] == null || m_Anchors[i + 1] == null)
                        continue;
                    var startPos = m_Anchors[i].position;
                    var endPos = m_Anchors[i + 1].position;
                    var curve = new CatenaryCurve(startPos, endPos, m_Catenary, unitLength);
                    var combines = new List<CombineInstance>();
                    var rotate = Quaternion.identity;
                    switch (m_Axis)
                    {
                        case Axis.X: rotate *= Quaternion.Euler(0, 90f, 0); break;
                        case Axis.Y: rotate *= Quaternion.Euler(90f, 0, 0); break;
                    }
                    var time = 0f;
                    foreach (var convert in curve)
                    {
                        var mulColor = Color.white - m_Gradient.Evaluate(time);
                        var curveMesh = Instantiate(sourceMesh);
                        for (var j = 0; j < srcVetices.Length; ++j)
                        {
                            var vertexData = new VertexData() { position = srcVetices[j], normal = srcNormals[j] };
                            vertexData = convert(vertexData);
                            dstVertices[j] = vertexData.position;
                            dstNormals[j] = vertexData.normal;
                        }
                        curveMesh.vertices = dstVertices;
                        curveMesh.normals = dstNormals;
                        curveMesh.colors32 = Array.ConvertAll(srcColors, c => (Color32)(Color.white - (Color.white - c) * mulColor));
                        combines.Add(new CombineInstance() { mesh = curveMesh });
                        time = Mathf.Repeat(time + 0.1f, 1f);
                    }
                    var mesh = new Mesh();
                    mesh.CombineMeshes(combines.ToArray(), true, false, false);
                    foreach (var c in combines)
                        DestroyImmediate(c.mesh);
                    MeshUtility.Optimize(mesh);
                    mesh.UploadMeshData(true);
                    var go = new GameObject($"ProcedualMesh{i}") { hideFlags = HideFlags.NotEditable };
                    go.transform.SetParent(transform);
                    ++sp.arraySize;
                    using (var element = sp.GetArrayElementAtIndex(sp.arraySize - 1))
                        element.objectReferenceValue = go;
                    var meshFilter = go.AddComponent<MeshFilter>();
                    var renderer = go.AddComponent<MeshRenderer>();
                    meshFilter.sharedMesh = mesh;
                    renderer.sharedMaterials = m_SourceMeshRenderer.sharedMaterials;
                    Undo.RegisterCreatedObjectUndo(mesh, "Create Mesh");
                    Undo.RegisterCreatedObjectUndo(go, "Create GameObject");
                }
                so.ApplyModifiedProperties();
            }
        }

        void OnDrawGizmos()
        {
            if (m_Anchors.Length < 2 || m_SourceMeshRenderer == null)
                return;
            var sourceMesh = m_SourceMeshRenderer.GetComponent<MeshFilter>().sharedMesh;
            if (sourceMesh == null)
                return;
            var bounds = sourceMesh.bounds;
            var unitLength = 0f;
            switch (m_Axis)
            {
                case Axis.X: unitLength = bounds.size.x; break;
                case Axis.Y: unitLength = bounds.size.y; break;
                case Axis.Z: unitLength = bounds.size.z; break;
                default: throw new System.ArgumentOutOfRangeException(nameof(m_Axis));
            }
            Gizmos.color = Color.blue;
            for (var i = 0; i < m_Anchors.Length - 1; ++i)
            {
                if (m_Anchors[i] == null || m_Anchors[i + 1] == null)
                    continue;
                var startPos = m_Anchors[i].position;
                var endPos = m_Anchors[i + 1].position;
                var curve = new CatenaryCurve(startPos, endPos, m_Catenary, unitLength);
                var vertA = new VertexData() { position = unitLength * 0.5f * Vector3.right };
                var vertB = new VertexData() { position = unitLength * 0.5f * Vector3.left };
                foreach (var convert in curve)
                {
                    Gizmos.DrawLine(convert(vertA).position, convert(vertB).position);
                }
            }
        }

        struct VertexData
        {
            public Vector3 position;
            public Vector3 normal;
        }

        sealed class CatenaryCurve : IEnumerable<Func<VertexData, VertexData>>
        {
            static float Asinh(float x) => Mathf.Log(x + Mathf.Sqrt(x * x + 1f));
            static float Acosh(float x) => Mathf.Log(x + Mathf.Sqrt(x * x - 1f));
            static float Sinh(float x) => (Mathf.Exp(x) - Mathf.Exp(-x)) * 0.5f;
            static float Cosh(float x) => (Mathf.Exp(x) + Mathf.Exp(-x)) * 0.5f;

            readonly Vector3 m_FromPos;
            readonly Vector3 m_ToPos;
            readonly float m_Catenary;
            readonly float m_UnitLength;

            public CatenaryCurve(Vector3 from, Vector3 to, float catenary, float unitLength)
            {
                m_FromPos = from;
                m_ToPos = to;
                m_Catenary = catenary;
                m_UnitLength = unitLength;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerator<Func<VertexData, VertexData>> GetEnumerator()
            {
                var hDirection = new Vector3(m_ToPos.x - m_FromPos.x, 0, m_ToPos.z - m_FromPos.z);
                var hDistance = hDirection.magnitude;
                hDirection /= hDistance;
                var vDistance = m_ToPos.y - m_FromPos.y;
                var a = m_Catenary * Asinh(vDistance / m_Catenary * 0.5f / Sinh(hDistance / m_Catenary * 0.5f)) - hDistance * 0.5f;
                var b = hDistance + a;
                var offset = Sinh(a / m_Catenary);
                var totalLength = m_Catenary * (Sinh(b / m_Catenary) - offset);
                var count = Mathf.RoundToInt(totalLength / m_UnitLength);
                var length = totalLength / count;
                var lScale = length / m_UnitLength;
                Func<float, Vector3> catenaryFunc = (float x) => x * hDirection + m_Catenary * Cosh(x / m_Catenary) * Vector3.up;
                var origin = m_FromPos - catenaryFunc(a);
                var biHDirection = new Vector3(-hDirection.z, 0, hDirection.x);
                for (var i = 0; i < count; ++i)
                {
                    yield return (VertexData vert) =>
                    {
                        var pos = vert.position;
                        var x = m_Catenary * Asinh((length * (i + 0.5f) + pos.x * lScale) / m_Catenary + offset);
                        vert.position = origin + catenaryFunc(x);
                        var normal = (-Sinh(x / m_Catenary) * hDirection + Vector3.up).normalized;
                        vert.position += pos.z * biHDirection + pos.y * normal;
                        vert.normal = Quaternion.LookRotation(biHDirection, normal) * vert.normal;
                        return vert;
                    };
                }
            }
        }

        [CustomEditor(typeof(CatenaryGenerator))]
        [CanEditMultipleObjects]
        sealed class CatenaryGeneratorInspector : Editor
        {
            public override void OnInspectorGUI()
            {
                base.OnInspectorGUI();
                var catenaryGenerator = (target as CatenaryGenerator);
                using (new EditorGUI.DisabledScope(catenaryGenerator.m_Anchors.Length < 2 || catenaryGenerator.m_SourceMeshRenderer == null))
                {
                    if (GUILayout.Button("Generate"))
                        (target as CatenaryGenerator).RecalculateMesh();
                }
            }
        }
    }
}
#endif
