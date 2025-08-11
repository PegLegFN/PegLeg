shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float progress : hint_range(0,1) = 1;
uniform float tileScale : hint_range(0,1) = 0.15;
uniform float tileSpeed : hint_range(0,1) = 0.1;
uniform float tileSpeedRatio : hint_range(0,1) = 0.5;
uniform bool useAlpha=true;
uniform sampler2D pattern;
uniform vec4 tint:source_color = vec4(1.0);

void fragment() {
	vec2 adaptedUV = adaptToAspect(ControlSize, vec2(0.0,0.5),1.0/vec2(textureSize(pattern, 0)),UV,true);

	if(COLOR.r > 0.5)
	{
		vec2 fixedUV = (vec2(adaptedUV.x*progress, adaptedUV.y)*tileScale)+vec2(TIME*-tileSpeed, TIME*-tileSpeed*tileSpeedRatio);
		fixedUV.x =mod(fixedUV.x, 1.0);
		fixedUV.y =mod(fixedUV.y, 1.0);
		vec4 tex = texture(pattern, fixedUV);
		float texFactor = useAlpha ? tex.a:tex.r;
		COLOR.rgb = ((texFactor*0.6)+0.4)*tint.rgb;
	}
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
