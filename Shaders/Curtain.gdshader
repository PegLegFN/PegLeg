shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"
#include "Includes/RotateVector.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 ShiftDirection = vec2(0.5,0.5);
uniform bool Fit;
uniform float Scale = 2.0f;
uniform float Speed = 0.2f;
uniform float Progress :hint_range(0.0, 1.0, 0.01) = 0;
uniform float Rotate :hint_range(0.0, 100.0, 0.01) = 30.0;
uniform bool ClipOOB;
global uniform bool GlobalCorners;

varying vec4 modulate;

void vertex()
{
	modulate=COLOR;
}

void fragment() {
	vec2 texRes=1.0/TEXTURE_PIXEL_SIZE;
	
	vec2 adaptedUV = adaptToAspect(ControlSize, ShiftDirection, texRes, UV, Fit)*Scale;
	//adaptedUV+=vec2(mod(TIME*-(Speed), Scale));
	COLOR = texture(TEXTURE, rotateVector2(adaptedUV, Rotate, vec2(1)));

	if(Fit && ClipOOB)
	{
		COLOR.a*=1.0-ceil(max(adaptedUV.x-1.0,adaptedUV.y-1.0));
		COLOR.a*=ceil(min(adaptedUV.x,adaptedUV.y));
	}
	COLOR.rgb *= vec3(COLOR.a);
	COLOR.a = 1.0;
	COLOR*=modulate;
	
	
	float smallest=min(ControlSize.x, ControlSize.y);
	float largest=max(ControlSize.x, ControlSize.y);
	float upperBound = 0.5*largest/smallest;

	vec2 fitUV = adaptToAspect(ControlSize, vec2(0.5), vec2(1), UV, true);
	float gradient = distance(fitUV, vec2(0.5));
	gradient*=20.0;
	gradient-=(Progress*(upperBound*20.0+5.0))-1.0;
	COLOR.a*=gradient;
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}
