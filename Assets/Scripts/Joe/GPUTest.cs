using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GPUTest : MonoBehaviour {
    [SerializeField]
    ComputeShader computeShader;
    
    [SerializeField, Range(10, 100)]
    int size = 10;

    [SerializeField]
    Material material;

    [SerializeField]
    Mesh mesh;

    ComputeBuffer positionsBuffer;

    static readonly int 
        positionsId = Shader.PropertyToID("_Positions"),
        sizeID = Shader.PropertyToID("_Size"),
        stepID = Shader.PropertyToID("_Step");

    private void OnEnable() {
        positionsBuffer = new ComputeBuffer(size * size, 3 * 4);
    }

    private void OnDisable() {
        positionsBuffer.Release();
        positionsBuffer = null;
    }

    private void Update() {
        UpdateFunctionOnGPU();
    }

    private void UpdateFunctionOnGPU() {
        float step = 2f / size;
        computeShader.SetInt(sizeID, size);
        computeShader.SetFloat(stepID, step);
        computeShader.SetBuffer(0, positionsId, positionsBuffer);

        int groups = Mathf.CeilToInt(size / 8f);
        computeShader.Dispatch(0, groups, groups, 1);

        material.SetBuffer(positionsId, positionsBuffer);
        material.SetFloat(stepID, step);
        
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * (2f + 2f / size));
        Graphics.DrawMeshInstancedProcedural(mesh, 0, material, bounds, positionsBuffer.count);
    }
}
