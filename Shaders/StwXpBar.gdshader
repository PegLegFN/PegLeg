shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float progress : hint_range(0,1) = 1;
uniform float tileScale : hint_range(0,1) = 0.15;
uniform float tileSpeed : hint_range(0,1) = 0.1;
uniform float tileSpeedRatio : hint_range(0,1) = 0.5;
uniform bool useAlpha=true;
uniform bool rainbow=false;
uniform sampler2D pattern;
uniform vec4 tint:source_color = vec4(1.0);

vec3 hsv_to_rgb(vec3 hsv) {
	vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
	vec3 p = abs(fract(hsv.xxx + K.xyz) * 6.0 - K.www);
	return hsv.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), hsv.y);
}

void fragment() {
	vec2 adaptedUV = adaptToAspect(ControlSize, vec2(0.0,0.5),1.0/vec2(textureSize(pattern, 0)),UV,true);

	if(COLOR.r > 0.5)
	{
		vec2 baseUV = vec2(adaptedUV.x*progress, adaptedUV.y)*tileScale;
		vec2 shiftedUV = baseUV+vec2(TIME*-tileSpeed, TIME*-tileSpeed*tileSpeedRatio);
		vec2 rainbowUV = baseUV*0.1+vec2(TIME*-tileSpeed*progress, TIME*-tileSpeed*tileSpeedRatio*progress);
		shiftedUV.x =mod(shiftedUV.x, 1.0);
		shiftedUV.y =mod(shiftedUV.y, 1.0);
		vec4 tex = texture(pattern, shiftedUV);
		float texFactor = useAlpha ? tex.a:tex.r;
		COLOR.rgb = ((texFactor*0.6)+0.4)*(rainbow ? hsv_to_rgb(vec3(rainbowUV.x-rainbowUV.y,progress,1)) : tint.rgb);
	}
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
