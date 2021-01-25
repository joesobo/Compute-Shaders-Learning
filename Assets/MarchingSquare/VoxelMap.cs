using UnityEngine;

[ExecuteInEditMode]
public class VoxelMap : MonoBehaviour {
    const int threadSize = 8;

    private static string[] fillTypeNames = { "Filled", "Empty" };
    private static string[] radiusNames = { "0", "1", "2", "3", "4", "5" };
    private static string[] stencilNames = { "Square", "Circle" };

    public float chunkSize = 2f;
    public int voxelResolution = 8;
    public int chunkResolution = 2;
    public VoxelChunk voxelGridPrefab;
    public bool useVoxelPoints;
    public ComputeShader shader;

    private VoxelChunk[] chunks;
    private float voxelSize, halfSize;
    private int fillTypeIndex, radiusIndex, stencilIndex;
    public int[] statePositions;

    ComputeBuffer verticeBuffer;
    ComputeBuffer triangleBuffer;
    ComputeBuffer triCountBuffer;
    ComputeBuffer stateBuffer;

    public bool updatingMap = false;

    private VoxelStencil[] stencils = {
        new VoxelStencil(),
        new VoxelStencilCircle()
    };

    private void Awake() {
        halfSize = chunkSize * 0.5f * chunkResolution;
        voxelSize = chunkSize / voxelResolution;
        statePositions = new int[(voxelResolution + 1) * (voxelResolution + 1)];
    }

    private void Update() {
        if ((Application.isPlaying && !updatingMap)) {
            updatingMap = true;

            CreateBuffers();

            chunks = new VoxelChunk[chunkResolution * chunkResolution];
            for (int i = 0, y = 0; y < chunkResolution; y++) {
                for (int x = 0; x < chunkResolution; x++, i++) {
                    CreateChunk(i, x, y);
                }
            }

            foreach (VoxelChunk chunk in chunks) {
                TriangulateChunk(chunk);
            }
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(chunkSize * chunkResolution, chunkSize * chunkResolution);

            if (!Application.isPlaying) {
                ReleaseBuffers();
            }
        }

        if (Input.GetMouseButton(0)) {
            RaycastHit hitInfo;
            if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hitInfo)) {
                if (hitInfo.collider.gameObject == gameObject) {
                    EditVoxels(transform.InverseTransformPoint(hitInfo.point));
                }
            }
        }
    }

    private void OnDestroy() {
        if (Application.isPlaying) {
            ReleaseBuffers();
        }
    }

    private void CreateChunk(int i, int x, int y) {
        VoxelChunk chunk = Instantiate(voxelGridPrefab) as VoxelChunk;
        chunk.Initialize(useVoxelPoints, voxelResolution, chunkSize);
        chunk.transform.parent = transform;
        chunk.transform.localPosition = new Vector3(x * chunkSize - halfSize, y * chunkSize - halfSize);
        chunks[i] = chunk;
        if (x > 0) {
            chunks[i - 1].xNeighbor = chunk;
        }
        if (y > 0) {
            chunks[i - chunkResolution].yNeighbor = chunk;
            if (x > 0) {
                chunks[i - chunkResolution - 1].xyNeighbor = chunk;
            }
        }
    }

    private void EditVoxels(Vector3 point) {
        int centerX = (int)((point.x + halfSize) / voxelSize);
        int centerY = (int)((point.y + halfSize) / voxelSize);

        int xStart = (centerX - radiusIndex - 1) / voxelResolution;
        if (xStart < 0) {
            xStart = 0;
        }
        int xEnd = (centerX + radiusIndex) / voxelResolution;
        if (xEnd >= chunkResolution) {
            xEnd = chunkResolution - 1;
        }
        int yStart = (centerY - radiusIndex - 1) / voxelResolution;
        if (yStart < 0) {
            yStart = 0;
        }
        int yEnd = (centerY + radiusIndex) / voxelResolution;
        if (yEnd >= chunkResolution) {
            yEnd = chunkResolution - 1;
        }

        VoxelStencil activeStencil = stencils[stencilIndex];
        activeStencil.Initialize(fillTypeIndex == 0, radiusIndex);

        int voxelYOffset = yEnd * voxelResolution;
        for (int y = yEnd; y >= yStart; y--) {
            int i = y * chunkResolution + xEnd;
            int voxelXOffset = xEnd * voxelResolution;
            for (int x = xEnd; x >= xStart; x--, i--) {
                activeStencil.SetCenter(centerX - voxelXOffset, centerY - voxelYOffset);
                chunks[i].Apply(activeStencil);
                TriangulateChunk(chunks[i]);
                voxelXOffset -= voxelResolution;
            }
            voxelYOffset -= voxelResolution;
        }
    }

    private void CreateBuffers() {
        int numPoints = (voxelResolution + 1) * (voxelResolution + 1);
        int numVoxelsPerResolution = voxelResolution - 1;
        int numVoxels = numVoxelsPerResolution * numVoxelsPerResolution;
        int maxTriangleCount = numVoxels * 3;

        if (!Application.isPlaying || (verticeBuffer == null || numPoints != verticeBuffer.count)) {
            if (Application.isPlaying) {
                ReleaseBuffers();
            }
            verticeBuffer = new ComputeBuffer(numPoints, sizeof(float) * 3);
            triangleBuffer = new ComputeBuffer(maxTriangleCount, sizeof(float) * 2 * 3, ComputeBufferType.Append);
            triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
            stateBuffer = new ComputeBuffer(numPoints, sizeof(int));
        }
    }

    private void ReleaseBuffers() {
        if (triangleBuffer != null) {
            verticeBuffer.Release();
            triangleBuffer.Release();
            triCountBuffer.Release();
            stateBuffer.Release();
        }
    }

    public void TriangulateChunk(VoxelChunk chunk) {
        chunk.mesh.Clear();

        // Compute Shader Here
        int numThreadsPerResolution = Mathf.CeilToInt(voxelResolution / threadSize);

        triangleBuffer.SetCounterValue(0);
        shader.SetBuffer(0, "_Vertices", verticeBuffer);
        shader.SetBuffer(0, "_Triangles", triangleBuffer);
        shader.SetBuffer(0, "_States", stateBuffer);
        shader.SetInt("_VoxelResolution", voxelResolution);
        shader.SetInt("_ChunkResolution", chunkResolution);

        SetupStates(chunk);
        stateBuffer.SetData(statePositions);

        shader.Dispatch(0, numThreadsPerResolution, numThreadsPerResolution, 1);

        ComputeBuffer.CopyCount(triangleBuffer, triCountBuffer, 0);
        int[] triCountArray = { 0 };
        triCountBuffer.GetData(triCountArray);
        int numTris = triCountArray[0];

        Triangle[] tris = new Triangle[numTris];
        triangleBuffer.GetData(tris, 0, 0, numTris);

        var vertices = new Vector3[numTris * 3];
        var triangles = new int[numTris * 3];

        for (int i = 0; i < numTris; i++) {
            for (int j = 0; j < 3; j++) {
                triangles[i * 3 + j] = i * 3 + j;

                var vertex = tris[i][j];
                vertex.x = vertex.x * chunkResolution * chunkSize;
                vertex.y = vertex.y * chunkResolution * chunkSize;

                vertices[i * 3 + j] = vertex;
            }
        }

        chunk.mesh.vertices = vertices;
        chunk.mesh.triangles = triangles;
        chunk.mesh.RecalculateNormals();
    }

    private void SetupStates(VoxelChunk chunk) {
        for (int i = 0, y = 0; y < voxelResolution; y++) {
            for (int x = 0; x < voxelResolution; x++, i++) {
                statePositions[y * voxelResolution + x + y] = chunk.voxels[i].state ? 1 : 0;
            }
        }

        for (int y = 0; y < voxelResolution; y++) {
            if (chunk.xNeighbor) {
                statePositions[y * voxelResolution + voxelResolution + y] = chunk.xNeighbor.voxels[y * voxelResolution].state ? 1 : 0;
            }
            else {
                statePositions[y * voxelResolution + voxelResolution + y] = -1;
            }
        }

        for (int x = 0; x < voxelResolution; x++) {
            if (chunk.yNeighbor) {
                statePositions[(voxelResolution + 1) * voxelResolution + x] = chunk.yNeighbor.voxels[x].state ? 1 : 0;
            }
            else {
                statePositions[(voxelResolution + 1) * voxelResolution + x] = -1;
            }
        }

        if (chunk.xyNeighbor) {
            statePositions[(voxelResolution + 1) * (voxelResolution + 1) - 1] = chunk.xyNeighbor.voxels[0].state ? 1 : 0;
        }
        else {
            statePositions[(voxelResolution + 1) * (voxelResolution + 1) - 1] = -1;
        }
    }

    struct Triangle {
#pragma warning disable 649 // disable unassigned variable warning
        public Vector2 a;
        public Vector2 b;
        public Vector2 c;

        public Vector2 this[int i] {
            get {
                switch (i) {
                    case 0:
                        return a;
                    case 1:
                        return b;
                    default:
                        return c;
                }
            }
        }
    }

    private void OnGUI() {
        GUILayout.BeginArea(new Rect(4f, 4f, 300f, 1000f));
        GUILayout.Label("Fill Type");
        fillTypeIndex = GUILayout.SelectionGrid(fillTypeIndex, fillTypeNames, 2);
        GUILayout.Label("Radius");
        radiusIndex = GUILayout.SelectionGrid(radiusIndex, radiusNames, 6);
        GUILayout.Label("Stencil");
        stencilIndex = GUILayout.SelectionGrid(stencilIndex, stencilNames, 2);
        GUILayout.EndArea();
    }
}