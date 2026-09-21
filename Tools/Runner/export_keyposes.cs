var src=new UnityEngine.Texture2D(2,2,UnityEngine.TextureFormat.RGBA32,false);
UnityEngine.ImageConversion.LoadImage(src,System.IO.File.ReadAllBytes("ArtProduction/Runner/Grok_MotionKeyposes.jpg"));
System.IO.Directory.CreateDirectory("ArtProduction/Runner/Keyposes");
for(int row=0;row<3;row++)for(int col=0;col<4;col++){
 var tex=new UnityEngine.Texture2D(256,256,UnityEngine.TextureFormat.RGBA32,false);
 for(int y=0;y<256;y++)for(int x=0;x<256;x++){
 float sx=col*448+x/256f*448,sy=(2-row)*336+(y-32)/256f*448;
 bool inside=y>=32&&y<224;
 tex.SetPixel(x,y,inside?src.GetPixelBilinear(sx/src.width,sy/src.height):UnityEngine.Color.white);
 }
 tex.Apply();System.IO.File.WriteAllBytes($"ArtProduction/Runner/Keyposes/pose_{row}_{col}.png",UnityEngine.ImageConversion.EncodeToPNG(tex));UnityEngine.Object.DestroyImmediate(tex);
}
UnityEngine.Object.DestroyImmediate(src);return "12 layout-sliced key poses exported through Unity";
