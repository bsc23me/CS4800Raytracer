using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class CameraRaytracer : MonoBehaviour
{

	// Struct dimensions
	public const int MaterialSize = 9;
	public const int TriangleSize = 7;
	public const int SphereSize = 4 + MaterialSize;
	public const int TextureStride = 3;

	// References
	public const string ModelPath = "Assets/Models/";
	public const uint NORMAL = 0x8080ffff;

	// Compute shader init
	private RenderTexture rt;
	[SerializeField] private ComputeShader rayTracingShader;
	[SerializeField] private int TILE_SIZE = 8;

	// World objects
	Camera cam;
	[SerializeField] GameObject SceneObj;

	// Render variables
	[SerializeField] private bool sunLight = true;
	[SerializeField] private Light sun;
	[SerializeField] bool useBB = true;
	[SerializeField] bool fastMath = false;

	static int TEXTURE_WIDTH = 1024;
	static int TEXTURE_HEIGHT = 1024;
	static long TEXTURE_SIZE = TEXTURE_WIDTH * TEXTURE_HEIGHT * sizeof(int);

	// Compute Buffers
	ComputeBuffer vertexBuffer;
	ComputeBuffer normalBuffer;
	ComputeBuffer UVBuffer;
	ComputeBuffer meshBuffer;
	ComputeBuffer BBBuffer;
	ComputeBuffer IndicesBuffer;

	// Loading Lists
	List<PMesh> meshes;
	List<Vector3> staticVertices;
	List<Vector3> staticNormals;
	List<Vector2> staticUVs;
	List<Vector3Int> staticTriangles;
	List<Texture2D> textures; Texture3D staticTextures;
	List<BoundingBox> staticBoundingBoxes;
	List<PMesh> staticIndices;

	private void OnEnable()
	{
		// List init
		staticVertices = new();
		staticNormals = new();
		staticUVs = new();
		staticTriangles = new();
		textures = new();
		staticBoundingBoxes = new();
		staticIndices = new() { new PMesh(0, 0, 0, 0, 0) };

		LoadScene();

		// Buffer init
		vertexBuffer = new ComputeBuffer(staticVertices.Count, 3 * sizeof(float));
		normalBuffer = new ComputeBuffer(staticNormals.Count, 3 * sizeof(float));
		UVBuffer = new ComputeBuffer(staticUVs.Count, 2 * sizeof(float));
		meshBuffer = new ComputeBuffer(staticTriangles.Count, 3 * sizeof(int));
		IndicesBuffer = new ComputeBuffer(staticIndices.Count, 5 * sizeof(int));
		BBBuffer = new ComputeBuffer(staticBoundingBoxes.Count, 6 * sizeof(float));

		// Buffer Loading
		vertexBuffer.SetData(staticVertices);
		normalBuffer.SetData(staticNormals);
		UVBuffer.SetData(staticUVs);
		meshBuffer.SetData(staticTriangles);
		IndicesBuffer.SetData(staticIndices);
		BBBuffer.SetData(staticBoundingBoxes);
	}

	private void OnDisable()
	{
		// Buffer Release
		if (vertexBuffer != null)
			vertexBuffer.Release();
		if (normalBuffer != null)
			normalBuffer.Release();
		if (UVBuffer != null)
			UVBuffer.Release();
		if (meshBuffer != null)
			meshBuffer.Release();
		if (IndicesBuffer != null)
			IndicesBuffer.Release();
		if (BBBuffer != null)
			BBBuffer.Release();
	}

	// Start is called before the first frame update
	void Awake() => cam = GetComponent<Camera>();

	// Update is called once per frame
	void Update()
	{
		sunLight = Input.GetKeyDown(KeyCode.Alpha1) ? !sunLight : sunLight;
		Time.timeScale = Input.GetKey(KeyCode.Alpha2) ? 0 : 1;
	}

	// Called each time a frame is rendered
	private void OnRenderImage(RenderTexture source, RenderTexture destination)
	{
		SetShaderVars();
		Render(destination);
	}

	/// <summary>
	/// Runs the GPU dispatch and draws the RenderTexture to the screen
	/// </summary>
	/// <param name="destination"></param>
	void Render(RenderTexture destination)
	{
		InitRenderTexture();

		rayTracingShader.SetTexture(0, "Result", rt);
		int threadGroupsX = Mathf.CeilToInt(Screen.width / TILE_SIZE);
		int threadGroupsY = Mathf.CeilToInt(Screen.height / TILE_SIZE);
		rayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
		
		Graphics.Blit(rt, destination);
	}
	
	/// <summary>
	/// Passes all of the shader and scene variables to the GPU
	/// </summary>
	void SetShaderVars()
	{
		// Camera vars (update every frame)
		rayTracingShader.SetMatrix("CameraToWorldMat", cam.cameraToWorldMatrix);
		rayTracingShader.SetMatrix("InversePerspectiveProjMat", cam.projectionMatrix.inverse);

		// Scene vars
		Vector3 l = sun.transform.forward;
		rayTracingShader.SetVector("Sun", new Vector4(l.x, l.y, l.z, sun.intensity));
		rayTracingShader.SetBool("sunLight", sunLight);

		// Buffers
		rayTracingShader.SetBuffer(0, "Vertices", vertexBuffer);
		rayTracingShader.SetBuffer(0, "VNormals", normalBuffer);
		rayTracingShader.SetBuffer(0, "UVs", UVBuffer);
		rayTracingShader.SetBuffer(0, "Meshes", meshBuffer);
		rayTracingShader.SetBuffer(0, "BoundingBoxes", BBBuffer);
		rayTracingShader.SetTexture(0, "Materials", staticTextures);
		rayTracingShader.SetBuffer(0, "MeshIndex", IndicesBuffer);
		rayTracingShader.SetInt("meshCount", staticIndices.Count);

		// Rendering vars
		rayTracingShader.SetBool("fastMath", fastMath);
		rayTracingShader.SetBool("useBB", useBB);

	}

	/// <summary>
	/// Creates the render texture which is sent to the GPU and is displayed on the screen
	/// </summary>
	void InitRenderTexture()
	{
		if(rt == null || rt.width != Screen.width || rt.height != Screen.height)
		{
			if(rt != null)
				rt.Release();

			rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
			rt.enableRandomWrite = true;
			rt.Create();
		}
	}

	#region structure constructors

	/// <summary>
	/// converts an RGBA uint color to a texture for the GPU
	/// </summary>
	/// <param name="size">how large the texture is (square)</param>
	/// <param name="color">the color to place across the entire texture</param>
	/// <returns>the texture created</returns>
	unsafe Texture2D createTexture(int size, uint color)
	{
		Texture2D tex = new Texture2D(size, size);
		byte[] data = new byte[size * size * sizeof(int)];
		byte* colorptr = (byte*)&color;
		for (long i = 0; i < size * size; i++)
			for (int j = 0; j < sizeof(int); j++)
				data[(i * sizeof(int)) + j] = colorptr[3 - j]; // little endian uint -> byte ordering
		tex.SetPixelData(data, 0);
		tex.Apply();
		return tex;
	}

	/// <summary>
	/// fills a texture space with a single byte
	/// </summary>
	/// <param name="size">how large the texture is (square)</param>
	/// <param name="color">the byte to place across the entire texture</param>
	/// <returns>the texture created</returns>
	[Obsolete]
	Texture2D createTexture(int size, byte color)
	{
		Texture2D tex = new Texture2D(size, size);
		byte[] data = new byte[size * size * sizeof(int)];
		for (long i = 0; i < size * size * sizeof(int); i += sizeof(int))
			for (int j = 0; j < sizeof(int); j++)
				data[i + j] = color;
		tex.SetPixelData(data, 0);
		tex.Apply();
		return tex;
	}

	/// <summary>
	/// Rotates a vector using euler angles about ZYX in that order
	/// </summary>
	/// <param name="vec">the vector to rotate</param>
	/// <param name="rotation">the euler angles in radians</param>
	/// <returns>the rotated vector</returns>
	Vector3 rotateVector(Vector3 vec, Vector3 rotation)
	{
		// axis rotation matricies
		float x, y, z;
		Vector3[] rotX = new Vector3[] {
			new Vector3(0, Mathf.Cos(rotation.x), -Mathf.Sin(rotation.x)),
			new Vector3(0, Mathf.Sin(rotation.x), Mathf.Cos(rotation.x)) };

		Vector3[] rotY = new Vector3[] {
			new Vector3(Mathf.Cos(rotation.y), 0, Mathf.Sin(rotation.y)) ,
			new Vector3(-Mathf.Sin(rotation.y), 0, Mathf.Cos(rotation.y))};

		Vector3[] rotZ = new Vector3[] {
			new Vector3(Mathf.Cos(rotation.z), -Mathf.Sin(rotation.z), 0),
			new Vector3(Mathf.Sin(rotation.z), Mathf.Cos(rotation.z), 0) };
		
		// rotate the vector
		// about z-axis
		x = Vector3.Dot(vec, rotZ[0]);
		y = Vector3.Dot(vec, rotZ[1]);
		z = vec.z;
		Vector3 v = new Vector3(x, y, z);

		// about y-axis
		x = Vector3.Dot(v, rotY[0]);
		z = Vector3.Dot(v, rotY[1]);
		v = new Vector3(x, y, z);

		// about x-axis
		y = Vector3.Dot(v, rotX[0]);
		z = Vector3.Dot(v, rotX[1]);

		return new Vector3(x, y, z);
	}

	/// <summary>
	/// rotates an array of vectors
	/// </summary>
	/// <param name="vec">the vectors to rotate</param>
	/// <param name="rotation">the rotation</param>
	/// <returns>the rotated vectors</returns>
	Vector3[] rotateVectors(Vector3[] vec, Vector3 rotation)
	{
		List<Vector3> rotVectors = new();
		foreach(Vector3 v in vec)
			rotVectors.Add(rotateVector(v, rotation));
		return rotVectors.ToArray();
	}
	
	/// <summary>
	/// Transform a point in 3D space (pseudo-affine)
	/// </summary>
	/// <param name="vertex">point/vertex to transform</param>
	/// <param name="position">position to offset the point from</param>
	/// <param name="rotation">roatation of the point</param>
	/// <param name="rotateAbout">point to rotate around</param>
	/// <param name="scale">scale the point away from the origin (DOES NOT HANDLE NEGATIVE VALUES)</param>
	/// <returns>the transformed point</returns>
	Vector3 transformVert(Vector3 vertex, Vector3 position, Vector3 rotation, Vector3 rotateAbout, Vector3 scale)
	{
		Vector3 v = vertex;
		v -= rotateAbout;
		v = rotateVector(v, rotation);
		v += rotateAbout;
		v.x *= scale.x;
		v.y *= scale.y;
		v.z *= scale.z;
		v += position;
		return v;
	}
	
	/// <summary>
	/// Transforms an array of points
	/// </summary>
	/// <param name="vertices">the array of points/vertices</param>
	/// <param name="position">see transformVert</param>
	/// <param name="rotation">see transformVert</param>
	/// <param name="rotateAbout">see transformVert</param>
	/// <param name="scale">see transformVert</param>
	/// <returns>an array of the transformed points</returns>
	Vector3[] transformVerts(Vector3[] vertices, Vector3 position, Vector3 rotation, Vector3 rotateAbout, Vector3 scale)
	{
		List<Vector3> verts = new List<Vector3>();
		foreach (Vector3 vert in vertices)
			verts.Add(transformVert(vert, position, rotation, rotateAbout, scale));
		return verts.ToArray();
	}

	/// <summary>
	/// Tranforms a mesh in during runtime
	/// </summary>
	/// <param name="obj">child object index</param>
	/// <param name="rotation">rotation</param>
	/// <param name="rotateAbout">point to rotate around</param>
	[Obsolete]
	void transformMesh(int obj, Vector3 rotation, Vector3 rotateAbout)
	{
		Transform t = SceneObj.transform.GetChild(obj);
		List<Vector3> mesh = staticVertices.GetRange(staticIndices[obj].verts, staticIndices[obj + 1].verts - staticIndices[obj].verts);
		List<Vector3> normals = staticNormals.GetRange(staticIndices[obj].normals, staticIndices[obj + 1].normals - staticIndices[obj].normals);
		Vector3[] transformedMesh = transformVerts(mesh.ToArray(), Vector3.zero, rotation, t.position + rotateAbout, Vector3.one);
		Vector3[] transformedNormals = rotateVectors(normals.ToArray(), rotation);
		for (int i = 0; i < mesh.Count; i++)
			staticVertices[staticIndices[obj].verts + i] = transformedMesh[i];
		for (int i = 0; i < normals.Count; i++)
			staticNormals[staticIndices[obj].normals + i] = transformedNormals[i];
	}

	#endregion

	#region IO

	/// <summary>
	/// Loads each object placed under the SceneObj and stores the info about each mesh and texture in the static lists
	/// </summary>
	void LoadScene()
	{
		foreach(MeshFilter meshFilter in SceneObj.GetComponentsInChildren<MeshFilter>(false))
		{
			Transform child = meshFilter.transform;
			MeshRenderer renderer = child.GetComponent<MeshRenderer>();

			if (child.gameObject.activeSelf && renderer)
			{
				LoadMesh(meshFilter.mesh, child, (textures.Count / TextureStride) + 1);

				uint[] defaults = LoadColors(renderer);

				LoadTexture(renderer.material, "_MainTex", defaults[0]);
				LoadTexture(renderer.material, "_BumpMap", NORMAL);
				LoadTexture(renderer.material, "_MetallicGlossMap", defaults[1]);
			}
		}

		ConvertTextures();
	}

	#region Meshes

	/// <summary>
	/// Loads a mesh with optional transform information and stores it's data in the respective static lists
	/// </summary>
	/// <param name="mesh">the mesh to load</param>
	/// <param name="position">the position of the mesh</param>
	/// <param name="rotation">the rotation of the mesh</param>
	/// <param name="rotateAbout">the point to rotate about</param>
	/// <param name="scale">the scale of the mesh</param>
	/// <param name="material">the mesh's material index</param>
	void LoadMesh(Mesh mesh, Vector3 position, Vector3 rotation, Vector3 rotateAbout, Vector3 scale, int material = -1)
	{
		Vector3 rot = rotation * Mathf.Deg2Rad;

		Vector3[] vertices = transformVerts(mesh.vertices, position, rot, rotateAbout, scale);
		staticVertices.AddRange(vertices);
		staticNormals.AddRange(rotateVectors(mesh.normals, rot));
		staticUVs.AddRange(mesh.uv);
		staticTriangles.AddRange(LoadFaces(mesh.triangles));

		PMesh pMesh = new(mesh.vertexCount, mesh.normals.Length, mesh.uv.Length, mesh.triangles.Length/3, material);
		staticIndices.Add(pMesh + staticIndices[staticIndices.Count - 1]);

		staticBoundingBoxes.Add(DefineBoundingBox(vertices));

		//Debug.Log($"Stored {pMesh.verts} Vertices from the {staticIndices.Count - 1}th Mesh; Total: {staticIndices[staticIndices.Count - 1].verts}");
	}
	
	/// <summary>
	/// Assumes no rotate about point
	/// </summary>
	void LoadMesh(Mesh mesh, Vector3 position, Vector3 rotation, Vector3 scale, int material = -1)
	{
		LoadMesh(mesh, position, rotation, Vector3.zero, scale, material);
	}

	/// <summary>
	/// Load Mesh with a Unity transform instead of individual parameters
	/// </summary>
	/// <param name="mesh">the mesh to be loaded</param>
	/// <param name="transform">the transform of the mesh</param>
	/// <param name="material">the material index</param>
	void LoadMesh(Mesh mesh, Transform transform, int material = -1)
	{
		LoadMesh(mesh, transform.position, transform.eulerAngles, transform.localScale, material);
	}

	/// <summary>
	/// Load a mesh with no transformation
	/// </summary>
	/// <param name="mesh">the mesh to load</param>
	void LoadMesh(Mesh mesh)
	{
		LoadMesh(mesh, Vector3.zero, Vector3.zero, Vector3.one);
	}

	#endregion

	/// <summary>
	/// Converts a triangle struct to the new integer index face format
	/// </summary>
	/// <param name="tri">the triangle</param>
	/// <returns>the vertex indices as an int3</returns>
	Vector3Int ConvertFace(Triangle tri)
	{
		return tri.points;
	}

	/// <summary>
	/// Loads a face from a set of ints
	/// </summary>
	/// <param name="verts">int array holding the vertex indices</param>
	/// <returns>the same values in a Vector3Int for easy GPU transfer</returns>
	Vector3Int LoadFace(int[] verts)
	{
		return new Vector3Int(verts[0], verts[1], verts[2]);
	}

	/// <summary>
	/// Loads an array of faces
	/// </summary>
	/// <param name="verts">the set of vertex indices</param>
	/// <returns>the array of int3 faces</returns>
	Vector3Int[] LoadFaces(int[] verts)
	{
		List<Vector3Int> tris = new List<Vector3Int>();
		for(int i = 0; i < verts.Length; i+=3)
			tris.Add(LoadFace(new int[] { verts[i], verts[i + 1], verts[i+2] }));

		return tris.ToArray();
	}

	/// <summary>
	/// Converts the static list of textures to a texture 3D for the GPU
	/// </summary>
	void ConvertTextures()
	{
		// Convert texture array to 3D texture to pass to GPU => put in LoadTextures() => LoadScene() ?
		byte[] pixels = new byte[textures.Count * TEXTURE_SIZE];
		for (int i = 0; i < textures.Count; i++)
			Array.Copy(textures[i].GetRawTextureData(), 0, pixels, i * TEXTURE_SIZE, TEXTURE_SIZE);

		staticTextures = new Texture3D(TEXTURE_WIDTH, TEXTURE_HEIGHT, textures.Count, TextureFormat.RGBA32, false);
		staticTextures.SetPixelData(pixels, 0);
		staticTextures.Apply();
	}

	/// <summary>
	/// Loads a texture from a material
	/// </summary>
	/// <param name="material">the material to load from</param>
	/// <param name="map">the name of the specific map in the material properties</param>
	/// <param name="defaultColor">if the given map is incompatible with the texture3D or no map is provided
	/// this parameter will be used to create a new map in its place</param>
	void LoadTexture(Material material, string map, uint defaultColor)
	{
		Texture2D tex = (Texture2D) material.GetTexture(map);
		if (!tex)
			textures.Add(createTexture(TEXTURE_HEIGHT, defaultColor));
		else if (tex.height != TEXTURE_HEIGHT)
			textures.Add(createTexture(TEXTURE_HEIGHT, tex.GetPixelData<uint>(0)[0]));
		else
			textures.Add(tex);
	}

	/// <summary>
	/// Constructs default colors from a material's properties if some maps are not specified
	/// </summary>
	/// <param name="renderer">the mesh renderer providing the material</param>
	/// <returns>int[] { Albedo, MetallicGloss }</returns>
	uint[] LoadColors(MeshRenderer renderer)
	{
		// Albedo
		Color baseColor = renderer.material.GetColor("_Color");
		Vector4 colorVector = new Vector4(baseColor.r, baseColor.g, baseColor.b, baseColor.a);
		colorVector *= 255;
		uint color = ((uint)colorVector.x << 24) + ((uint)colorVector.y << 16) + ((uint)colorVector.z << 8) + (uint)colorVector.w;

		// Metallic Gloss
		float gloss = renderer.material.GetFloat("_Glossiness");
		float metl = renderer.material.GetFloat("_Metallic");
		uint glossi = (uint)Mathf.FloorToInt(gloss * 255);
		uint metli = (uint)Mathf.FloorToInt(metl * 255);
		glossi <<= 16;
		metli <<= 24;
		uint mgm = glossi + metli;

		return new uint[] { color, mgm };
	}

	/// <summary>
	/// Given a set of vertices, specifies an upper and lower bound which guarentees all vertices in the set will be within
	/// </summary>
	/// <param name="vertices">the set of vertices used to define the bounding box</param>
	/// <returns>a bounding box struct which stores the 3D limits of the mesh</returns>
	BoundingBox DefineBoundingBox(Vector3[] vertices)
	{
		float x1 = float.PositiveInfinity, y1 = float.PositiveInfinity, z1 = float.PositiveInfinity;
		float x2 = float.NegativeInfinity, y2 = float.NegativeInfinity, z2 = float.NegativeInfinity;
		foreach (Vector3 vert in vertices)
		{
			x1 = Mathf.Min(x1, vert.x);
			y1 = Mathf.Min(y1, vert.y);
			z1 = Mathf.Min(z1, vert.z);

			x2 = Mathf.Max(x2, vert.x);
			y2 = Mathf.Max(y2, vert.y);
			z2 = Mathf.Max(z2, vert.z);
		}
		BoundingBox bb;
		bb.lowerBound = new Vector3(x1, y1, z1);
		bb.upperBound = new Vector3(x2, y2, z2);
		return bb;
	}

	#region Deprecated

	/// <summary>
	/// Loads a mesh directly from a file
	/// </summary>
	/// <param name="path">the path of the .obj file</param>
	/// <param name="material">the PMaterial of the mesh (Obsolete)</param>
	/// <param name="position">the position of the mesh</param>
	/// <param name="rotation">the rotation of the mesh</param>
	[Obsolete("LoadMesh(string,...) is deprecated. Please use LoadMesh(Mesh,...) to utilize Unity's builtin file IO.")]
	void LoadMesh(string path, PMaterial material, Vector3 position, Vector3 rotation)
	{
		StreamReader streamReader = new StreamReader(ModelPath + path);
		int verts = 0, normals = 0, uvs = 0, faces = 0;
		string data = streamReader.ReadToEnd();
		string[] lines = data.Split("\n");
		for(int i = 0; i < lines.Length; i++)
		{
			if (lines[i].StartsWith("v "))
			{
				verts++;
				Vector3 vert = transformVert(LoadVector(lines[i]), position, rotation, Vector3.zero, Vector3.one);
				//Vector3 vert = LoadVector(lines[i]);
				staticVertices.Add(vert);
			}
			else if (lines[i].StartsWith("vt"))
			{
				uvs++;
				staticUVs.Add(LoadVector2(lines[i]));
			}
			else if (lines[i].StartsWith("vn"))
			{
				normals++;
				staticNormals.Add(rotateVector(LoadVector(lines[i]), rotation));
			}
			else if (lines[i].StartsWith("f "))
			{
				faces++;
				staticTriangles.Add(ConvertFace(LoadFace(lines[i])));
			}
		}
		PMesh mesh = new PMesh(verts, normals, uvs, faces, -1);
		if (staticIndices.Count > 0)
			staticIndices.Add(mesh + staticIndices[staticIndices.Count-1]);
		else
			staticIndices.Add(mesh);
	}

	/// <summary>
	/// Loads a Vector3 from a .obj file format
	/// </summary>
	/// <param name="value">the data</param>
	/// <returns>the vector</returns>
	[Obsolete]
	Vector3 LoadVector(string value)
	{
		string[] values = value.Split(" ");
		float x = float.Parse(values[1]);
		float y = float.Parse(values[2]);
		float z = float.Parse(values[3]);
		return new Vector3(x, y, z);
	}

	/// <summary>
	/// Loads a Vector2 from a .obj file format
	/// </summary>
	/// <param name="value">the data</param>
	/// <returns>the vector</returns>
	[Obsolete]
	Vector2 LoadVector2(string value)
	{
		string[] values = value.Split(" ");
		float x = float.Parse(values[1]);
		float y = float.Parse(values[2]);
		return new Vector2(x, y);
	}

	/// <summary>
	/// Loads a Triangle struct from a .obj file format
	/// </summary>
	/// <param name="value"></param>
	/// <returns></returns>
	[Obsolete]
	Triangle LoadFace(string value)
	{

		Triangle tri = new Triangle();
		string[] verts = value.Split(" ");
		string[][] values = new string[verts.Length][];
		for (int i = 0; i < verts.Length; i++)
			values[i] = verts[i].Split("/");

		tri.points.x = int.Parse(values[1][0]) - 1;
		tri.points.y = int.Parse(values[2][0]) - 1;
		tri.points.z = int.Parse(values[3][0]) - 1;

		tri.uv.x = int.Parse(values[1][1]) - 1;
		tri.uv.y = int.Parse(values[2][1]) - 1;
		tri.uv.z = int.Parse(values[3][1]) - 1;

		tri.normal = int.Parse(values[1][2]) - 1;
		return tri;
	}

	#endregion

	#endregion

	#region debug
	void printTri(Triangle tri, Vector3[] verts)
	{
		Debug.Log($"Point A: {verts[tri.points.x]},\nPoint B: {verts[tri.points.y]},\nPoint C: {verts[tri.points.z]}");
	}

	#endregion
}

#region structs

struct PMaterial
{
	public Vector3 albedo;
	public float smoothness;
	public float metalic;
	public Vector3 emission;
	public float emissionStrength;
}

struct Triangle
{
	public Vector3Int points;
	public Vector3Int uv;
	public int normal;
}

public struct PMesh
{
	public int verts;
	public int normals;
	public int uvs;
	public int faces;
	public int material;
	
	public PMesh(int v,int n, int u,int f,int m)
	{
		verts = v;
		normals = n;
		uvs = u;
		faces = f;
		material = m;
	}

	public static PMesh operator +(PMesh p1, PMesh p2)
		=> new PMesh(p1.verts + p2.verts,p1.normals+p2.normals,p1.uvs+p2.uvs,p1.faces+p2.faces,p1.material+p2.material);

}

struct BoundingBox
{
	public Vector3 lowerBound;
	public Vector3 upperBound;
}

#endregion