shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 ShiftDirection = vec2(0.5,0.5);
uniform bool UseCorners;
uniform bool Fit;
uniform float CornerRadius = 10.0;
uniform bool ThreeColourGradient;
uniform vec4 FirstCol:source_color = vec4(1.0,0.0,0.0,1.0);
uniform vec4 SecondCol:source_color = vec4(1.0,1.0,0.0,1.0);
uniform vec4 ThirdCol:source_color = vec4(0.0,1.0,0.0,1.0);


varying vec4 modulate;

void vertex()
{
	modulate=COLOR;
}

void fragment() {
	vec2 texRes=1.0/TEXTURE_PIXEL_SIZE;
	vec2 adaptedUV = adaptToAspect(ControlSize, ShiftDirection,texRes,UV,Fit);
	
	float factor = texture(TEXTURE, adaptedUV).r;
	
	if(ThreeColourGradient)
	{
		factor*=2.0;
		COLOR = mix(FirstCol,mix(SecondCol, ThirdCol, clamp(factor-1.0,0,1)),clamp(factor,0,1));
	}
	else
	{
		COLOR = mix(FirstCol,SecondCol,factor);
	}

	if(UseCorners)
	{
		COLOR.a *= cornerMask(UV,CornerRadius,ControlSize);
	}
	COLOR*=modulate;
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}
