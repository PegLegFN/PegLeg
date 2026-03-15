shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float CornerRadius = 10.0;
uniform bool Simple;
uniform vec4 BGCol : source_color = vec4(1.0);
uniform vec4 TriCol1 : source_color = vec4(0.5);
uniform vec4 TriCol2 : source_color = vec4(0.5);
uniform vec4 TriCol3 : source_color = vec4(0.5);

varying vec4 modulate;

void vertex() {
	modulate = COLOR;
}

vec4 triangleShift(vec2 baseUV, float time, sampler2D tex)
{
	vec4 shifted = clamp(texture(tex, baseUV-vec2(time,0))*8.0*(1.0-(baseUV.x*0.25)), 0.0, 1.0);
	return clamp(shifted*(1.0-distance(baseUV.x*0.25,0)), 0.0, 1.0);
}

void fragment() {
	vec2 adaptedUV = adaptToAspect(ControlSize, vec2(0,0.5), 1.0/TEXTURE_PIXEL_SIZE, UV, true)*vec2(1);

	float dist = (1.0-(distance(UV/vec2(2,1),vec2(0,0.5))));
	
	COLOR = BGCol * dist;
	if(!Simple){
		COLOR.rgb = mix(COLOR.rgb, TriCol1.rgb, triangleShift(adaptedUV, TIME*0.2, TEXTURE).x*TriCol1.a);
		COLOR.rgb = mix(COLOR.rgb, TriCol2.rgb, triangleShift(adaptedUV, TIME*0.4, TEXTURE).y*TriCol2.a);
		COLOR.rgb = mix(COLOR.rgb, TriCol3.rgb, triangleShift(adaptedUV, TIME*0.6, TEXTURE).z*TriCol3.a);
	}
	else{
		vec4 shifted = triangleShift(adaptedUV, 0.0, TEXTURE);
		COLOR.rgb = mix(COLOR.rgb, TriCol1.rgb, shifted.x*TriCol1.a);
		COLOR.rgb = mix(COLOR.rgb, TriCol2.rgb, shifted.y*TriCol2.a);
		COLOR.rgb = mix(COLOR.rgb, TriCol3.rgb, shifted.z*TriCol3.a);
	}
	
	COLOR.a = cornerMask(UV,CornerRadius,ControlSize);
}
