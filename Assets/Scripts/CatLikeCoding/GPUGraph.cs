using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GPUGraph : MonoBehaviour {
    const int maxResolution = 1000;

    [SerializeField] ComputeShader computeShader = default;

    [SerializeField, Range(10, maxResolution)] int resolution = 10;

    [SerializeField] FunctionLibrary.FunctionName function = default;

    public enum TransitionMode { Cycle, Random }

    [SerializeField] TransitionMode transitionMode = TransitionMode.Cycle;

    [SerializeField, Min(0f)] float functionDuration = 1f, transitionDuration = 1f;

    [SerializeField] Material material = default;

    [SerializeField] Mesh mesh = default;

    float duration;
    bool transitioning;
    FunctionLibrary.FunctionName transitionFunction;

    ComputeBuffer positionsBuffer;
    static readonly int
        positionsId = Shader.PropertyToID("_Positions"),
        resolutionId = Shader.PropertyToID("_Resolution"),
        stepId = Shader.PropertyToID("_Step"),
        timeId = Shader.PropertyToID("_Time"),
		transitionProgressId = Shader.PropertyToID("_TransitionProgress");

    void OnEnable() {
        positionsBuffer = new ComputeBuffer(maxResolution * maxResolution, 3 * 4);
    }

    void OnDisable() {
        positionsBuffer.Release();
        positionsBuffer = null;
    }

    void Update() {
        duration += Time.deltaTime;
        if (transitioning) {
            if (duration >= transitionDuration) {
                duration -= transitionDuration;
                transitioning = false;
            }
        }
        else if (duration >= functionDuration) {
            duration -= functionDuration;
            transitioning = true;
            transitionFunction = function;
            PickNextFunction();
        }

        UpdateFunctionOnGPU();
    }

    void PickNextFunction() {
        function = transitionMode == TransitionMode.Cycle ?
            FunctionLibrary.GetNextFunction(function) :
            FunctionLibrary.GetRandomFunctionNameOtherThan(function);
    }

    void UpdateFunctionOnGPU() {
        float step = 2f / resolution;
        int groups = Mathf.CeilToInt(resolution / 8f);
        material.SetBuffer(positionsId, positionsBuffer);
        material.SetFloat(stepId, step);
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * (2f + 2f / resolution));

        computeShader.SetInt(resolutionId, resolution);
        computeShader.SetFloat(stepId, step);
        computeShader.SetFloat(timeId, Time.time);
        if (transitioning) {
			computeShader.SetFloat(
				transitionProgressId,
				Mathf.SmoothStep(0f, 1f, duration / transitionDuration)
			);
		}

        int kernelIndex = (int)function + (int)(transitioning ? transitionFunction : function) * FunctionLibrary.FunctionCount;
        computeShader.SetBuffer(kernelIndex, positionsId, positionsBuffer);

        computeShader.Dispatch(kernelIndex, groups, groups, 1);

        Graphics.DrawMeshInstancedProcedural(mesh, 0, material, bounds, resolution * resolution);
    }
}