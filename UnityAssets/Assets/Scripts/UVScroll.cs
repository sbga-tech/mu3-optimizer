using UnityEngine;

public class UVScroll : MonoBehaviour
{
	[SerializeField]
	private float _scrollSpeedU = 0.1f;

	[SerializeField]
	private float _scrollSpeedV = 0.1f;

	private Renderer _renderer;

	private MaterialPropertyBlock _mpb;

	private float _u;

	private float _v;

	private static int MainTexST;

	private void Awake()
	{
		MainTexST = Shader.PropertyToID("_MainTex_ST");
	}

	private void Start()
	{
		_renderer = GetComponent<Renderer>();
		_mpb = new MaterialPropertyBlock();
	}

	private void Update()
	{
		_u = Mathf.Repeat(_u + Time.deltaTime * _scrollSpeedU, 1f);
		_v = Mathf.Repeat(_v + Time.deltaTime * _scrollSpeedV, 1f);
		_renderer.GetPropertyBlock(_mpb);
		_mpb.SetVector(MainTexST, new Vector4(1f, 1f, _u, _v)); 
		_renderer.SetPropertyBlock(_mpb);
	}
}
