shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"
#include "Includes/RotateVector.gdshaderinc"
#include "Includes/ScaleVector.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float Shift:hint_range(0.0, 1.0) = 0.0;
uniform bool Fit;
uniform bool IsJamTrack;
uniform float CosmeticZoom:hint_range(0.0, 1.0) = 1.0;
uniform float CornerRadius = 10.0;
uniform float JamRadius = 3.0;
uniform sampler2D JamTexture;
uniform float time = 0.0;

varying vec4 Modulate;

void vertex()
{
	Modulate=COLOR;
}

void fragment() {
	vec2 texRes=1.0/TEXTURE_PIXEL_SIZE;
	vec2 adaptedUV = adaptToAspect(ControlSize, vec2(0.5,Shift),texRes,UV,Fit);
	
	if(IsJamTrack)
	{
		vec2 albumUV = adaptedUV;
		
		vec2 shrinkScale = ControlSize/(ControlSize-vec2(JamRadius*2.0));
		albumUV*=shrinkScale;
		albumUV-=(1.0/ControlSize)*JamRadius;
		
		vec4 albumColor = texture(TEXTURE, albumUV);
		albumColor.a *= cornerMask(albumUV,CornerRadius-JamRadius,ControlSize);
		
		COLOR = texture(JamTexture, rotateVector2(adaptedUV, 72.0*time, vec2(0.5)));
		COLOR.a *= cornerMask(UV,CornerRadius,ControlSize);
		COLOR.rgb = mix(COLOR.rgb, albumColor.rgb, albumColor.a);
	}
	else
	{
		COLOR = texture(TEXTURE, scaleVector2(adaptedUV,vec2(CosmeticZoom),vec2(0.5)));
		COLOR.a *= cornerMask(UV,CornerRadius,ControlSize);
		if(Fit)
		{
			//clip oob
			COLOR.a*=1.0-ceil(max(adaptedUV.x-1.0,adaptedUV.y-1.0));
			COLOR.a*=ceil(min(adaptedUV.x,adaptedUV.y));
		}
	}
	COLOR*=Modulate;
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}
