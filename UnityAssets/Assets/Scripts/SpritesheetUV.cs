using UnityEngine;

namespace MU3.Mod
{
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
    
        /// <summary>
        /// Compute and apply the UV rect for the given sprite index.
        /// </summary>
        public void Apply(int index)
        {
            if (_spritesheet == null || _renderer == null) return;
    
            Vector4 st = ComputeMainTexST(_spritesheet.width, _spritesheet.height,
                                           blockWidth, blockHeight, index, spritesPerRow);
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetVector(MainTexSTID, st);
            _renderer.SetPropertyBlock(_mpb);
        }
    
        /// <summary>
        /// Returns _MainTex_ST value (scaleX, scaleY, offsetX, offsetY) for a given sprite index.
        /// Grid order: left-to-right, top-to-bottom (index 0 = top-left cell).
        /// </summary>
        public static Vector4 ComputeMainTexST(int texWidth, int texHeight, 
                                                int cellW, int cellH, int index, int? spritesPerRow = null)
        {
            int cols = spritesPerRow ?? (texWidth / cellW);
            int rows = texHeight / cellH;
            if (cols <= 0 || rows <= 0) return new Vector4(1, 1, 0, 0);
    
            int col = index % cols;
            int row = index / cols;
    
            float scaleX = (float)cellW / texWidth;
            float scaleY = (float)cellH / texHeight;
    
            // UV origin is bottom-left; row 0 in grid = top row in UV
            float offsetX = col * scaleX;
            float offsetY = 1f - (row + 1) * scaleY;
    
            // _MainTex_ST layout: (scaleX, scaleY, offsetX, offsetY)
            return new Vector4(scaleX, scaleY, offsetX, offsetY);
        }
        
        int _lastIndex = -1;
        int _lastTexID = -1;
        void OnValidate()
        {
            Init();
            int texID = _spritesheet.GetInstanceID();
            if (spriteIndex == _lastIndex && texID == _lastTexID) return;
            _lastIndex = spriteIndex;
            _lastTexID = texID;
    
            Apply(spriteIndex);
        }
    }
}

