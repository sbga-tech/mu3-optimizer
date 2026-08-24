using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class UVSheetAnimElement
{
	private static int MainTex_ST;
	private static bool propertyIdInitialized;

	private MaterialPropertyBlock mpb;
	public enum e原点位置
	{
		左上_一般的 = 0,
		左下_Unity準拠 = 1
	}

	public enum eフレームレート
	{
		fps60 = 60,
		fps30 = 30,
		無制限 = 1
	}

	public bool Xと共通;

	public float X_上限 = 1f;

	public float X_下限 = -1f;

	public float Y_上限 = 1f;

	public float Y_下限 = -1f;

	public float Z_上限 = 1f;
	
	public float Z_下限 = -1f;

	private float X補正値;

	private float Y補正値;

	private float Z補正値;

	private float baseScaleX;

	private float baseScaleY;

	private float baseScaleZ;

	public float 速度 = 1f;

	public Renderer 対象オブジェクトのRenderer;

	public int 縦のコマ数;

	public int 横のコマ数;

	public int 切替までのフレーム数;

	public bool 逆再生;

	public int 開始コマ数;

	public e原点位置 UV座標原点;

	public eフレームレート 想定fps_エディタ用 = eフレームレート.fps60;

	private float w;

	private float h;

	private List<Vector2> offsetList = new List<Vector2>();

	private int frameCount;

	private int currentId;

	private void init_SinScale()
	{
		if (X_上限 < X_下限)
		{
			float x_上限 = X_上限;
			X_上限 = X_下限;
			X_下限 = x_上限;
		}
		if (Y_上限 < Y_下限)
		{
			float y_上限 = Y_上限;
			Y_上限 = Y_下限;
			Y_下限 = y_上限;
		}
		if (Z_上限 < Z_下限)
		{
			float z_上限 = Z_上限;
			Z_上限 = Z_下限;
			Z_下限 = z_上限;
		}
		if (Xと共通)
		{
			Y_上限 = X_上限;
			Z_上限 = X_上限;
			Y_下限 = X_下限;
			Z_下限 = X_下限;
		}
		X補正値 = (X_上限 - X_下限) / 2f;
		Y補正値 = (Y_上限 - Y_下限) / 2f;
		Z補正値 = (Z_上限 - Z_下限) / 2f;
		baseScaleX = 対象オブジェクトのRenderer.transform.localScale.x;
		baseScaleY = 対象オブジェクトのRenderer.transform.localScale.y;
		baseScaleZ = 対象オブジェクトのRenderer.transform.localScale.z;
	}

	private void update_SinScale()
	{
		float x = baseScaleX * (Mathf.Sin(Time.time * 360f * ((float)Math.PI / 180f) * 速度) * X補正値 + (X補正値 + X_下限));
		float y = baseScaleY * (Mathf.Sin(Time.time * 360f * ((float)Math.PI / 180f) * 速度) * Y補正値 + (Y補正値 + Y_下限));
		float z = baseScaleZ * (Mathf.Sin(Time.time * 360f * ((float)Math.PI / 180f) * 速度) * Z補正値 + (Z補正値 + Z_下限));
		対象オブジェクトのRenderer.transform.localScale = new Vector3(x, y, z);
	}

	public void init()
	{
		init_UVAnim();
		init_SinScale();
	}

	public void update()
	{
		update_UVAnim();
		update_SinScale();
	}

	public void init_UVAnim()
	{
		if (縦のコマ数 == 0 || 横のコマ数 == 0 || 対象オブジェクトのRenderer == null)
		{
			return;
		}
		if (!propertyIdInitialized)
		{
			MainTex_ST = Shader.PropertyToID("_MainTex_ST");
			propertyIdInitialized = true;
		}
		mpb = new MaterialPropertyBlock();
		w = 1f / (float)横のコマ数;
		h = 1f / (float)縦のコマ数;
		offsetList.Clear();
		if (UV座標原点 == e原点位置.左上_一般的)
		{
			int num = 縦のコマ数 - 1;
			while (0 <= num)
			{
				Vector2 item = new Vector2(0f, 0f);
				item.y = (float)num * h;
				for (int i = 0; i < 横のコマ数; i++)
				{
					item.x = (float)i * w;
					offsetList.Add(item);
				}
				num--;
			}
		}
		else
		{
			for (int j = 0; j < 縦のコマ数; j++)
			{
				Vector2 item2 = new Vector2(0f, 0f);
				item2.y = (float)j * h;
				for (int k = 0; k < 横のコマ数; k++)
				{
					item2.x = (float)k * w;
					offsetList.Add(item2);
				}
			}
		}
		currentId = 開始コマ数;
	}

	public void update_UVAnim()
	{
		if (縦のコマ数 == 0 || 横のコマ数 == 0 || 対象オブジェクトのRenderer == null)
		{
			return;
		}
		if (frameCount >= 切替までのフレーム数)
		{
			Vector2 value = offsetList[currentId];
			対象オブジェクトのRenderer.GetPropertyBlock(mpb);
			mpb.SetVector(MainTex_ST, new Vector4(w, h, value.x, value.y));
			対象オブジェクトのRenderer.SetPropertyBlock(mpb);
			if (逆再生)
			{
				if (currentId == 0)
				{
					currentId = offsetList.Count - 1;
				}
				else
				{
					currentId--;
				}
			}
			else if (currentId == offsetList.Count - 1)
			{
				currentId = 0;
			}
			else
			{
				currentId++;
			}
			frameCount = 0;
		}
		frameCount++;
	}
}
