using UnityEngine;

public class UVSheetAnim : MonoBehaviour
{
	[SerializeField]
	private UVSheetAnimElement[] elements;

	private void Awake()
	{
		start();
	}

	private void start()
	{
		UVSheetAnimElement[] array = elements;
		foreach (UVSheetAnimElement uVSheetAnimElement in array)
		{
			uVSheetAnimElement.init();
		}
	}

	public void Update()
	{
		UVSheetAnimElement[] array = elements;
		foreach (UVSheetAnimElement uVSheetAnimElement in array)
		{
			uVSheetAnimElement.update();
		}
	}
}
