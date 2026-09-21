using UnityEngine;
using UnityEngine.UI;

namespace Afterecho
{
    // Exact UI geometry; visual timing is computed from the same DSP clock as judgment.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RunnerRing : MaskableGraphic
    {
        public float thickness=3;
        public void SetDiameter(float diameter,float line,Color tint)
        {rectTransform.sizeDelta=Vector2.one*diameter;thickness=line;color=tint;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();float outer=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f;
            float inner=Mathf.Max(0,outer-thickness);const int segments=128;
            for(int i=0;i<=segments;i++)
            {
                float a=i*Mathf.PI*2/segments;Vector2 dir=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                vh.AddVert(dir*outer,color,new Vector2(0,0));vh.AddVert(dir*inner,color,new Vector2(1,1));
                if(i>0){int k=i*2;vh.AddTriangle(k-2,k,k-1);vh.AddTriangle(k,k+1,k-1);}
            }
        }
    }
}
