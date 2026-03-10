using UnityEngine;

namespace MU3.Mod;

public class SpritesheetUV : MonoBehaviour
{
    private Texture2D _spritesheet;

    public int spriteIndex;

    public int blockWidth;
    public int blockHeight;
    public int spritesPerRow;

    private static int MainTexSTID;

    MaterialPropertyBlock _mpb;
    Renderer _renderer;

    void Awake()
    {
        Init();
        Apply(spriteIndex);
    }

    void Init()
    {
        if (_mpb != null) return;
        MainTexSTID = Shader.PropertyToID("_MainTex_ST");
        _renderer = GetComponent<Renderer>();
        _spritesheet = _renderer.sharedMaterial.mainTexture as Texture2D;
        _mpb = new MaterialPropertyBlock();
    }

    public void Apply(int index)
    {
        if (_spritesheet == null || _renderer == null) return;

        var st = ComputeMainTexST(_spritesheet.width, _spritesheet.height,
                                       blockWidth, blockHeight, index, spritesPerRow);
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetVector(MainTexSTID, st);
        _renderer.SetPropertyBlock(_mpb);
    }
    
    
    public static Vector4 ComputeMainTexST(int texWidth, int texHeight, 
                                            int cellW, int cellH, int index, int? spritesPerRow = null)
    {
        var cols = spritesPerRow ?? (texWidth / cellW);
        var rows = texHeight / cellH;
        if (cols <= 0 || rows <= 0) return new Vector4(1, 1, 0, 0);

        var col = index % cols;
        var row = index / cols;

        var scaleX = (float)cellW / texWidth;
        var scaleY = (float)cellH / texHeight;

        // UV origin is bottom-left; row 0 in grid = top row in UV
        var offsetX = col * scaleX;
        var offsetY = 1f - (row + 1) * scaleY;

        return new Vector4(scaleX, scaleY, offsetX, offsetY);
    }
    
    int _lastIndex = -1;
    int _lastTexID = -1;
    void OnValidate()
    {
        Init();
        var texID = _spritesheet.GetInstanceID();
        if (spriteIndex == _lastIndex && texID == _lastTexID) return;
        _lastIndex = spriteIndex;
        _lastTexID = texID;

        Apply(spriteIndex);
    }
}