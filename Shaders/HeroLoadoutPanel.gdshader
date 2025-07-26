shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);

varying vec4 modulate;

void vertex()
{
	modulate=COLOR;
}

void fragment() {
	vec2 texRes=5.0/TEXTURE_PIXEL_SIZE;
	vec2 adaptedUV = adaptToAspect(ControlSize, vec2(0.0,0.5),texRes, UV, true)*0.5f;
	float triTex=clamp(texture(TEXTURE, adaptedUV).r, 0.0, 1.0);
	float triTexB=clamp(texture(TEXTURE, adaptedUV+vec2(0.0,0.5)).r, 0.0, 1.0);
	float scaledTriTex=triTex+round(-adaptedUV.x*2.0)/2.0;
	float weight = clamp((sin((TIME*2.0)+(scaledTriTex*0.5))*0.5)+0.5, 0.0, 1.0);
	//COLOR.rgb = vec3(triTex*0.15);
	COLOR.rgb = mix(modulate.rgb*(triTex*0.25+0.25), modulate.rgb*(triTexB*0.5+0.5), weight);
	COLOR.a = modulate.a*cornerMask(UV,5,ControlSize);
	//COLOR *= modulate;
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}
