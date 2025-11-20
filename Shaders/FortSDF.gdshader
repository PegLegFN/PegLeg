shader_type canvas_item;
#include "Includes/SDFHelpers.gdshaderinc"

uniform sampler2D blendFrom;
uniform float blendAmount:hint_range(0,1)=1;
uniform float threshold:hint_range(0,1) = 0.45;
uniform float smoothness:hint_range(0,1) = 0.1;
uniform bool msdf = false;
uniform bool skipSDF = false;

varying vec4 modulate;
void vertex() {
	modulate = COLOR;
}

void fragment() {
	vec4 source = texture(TEXTURE, UV);
	vec4 blend = texture(blendFrom, UV);

	COLOR = modulate;
	if(skipSDF){
		COLOR.a=source.r;
	}
	else if (msdf)
	{
		COLOR.a = blendMSDF(blend.rgb, source.rgb, blendAmount, threshold, smoothness);
	}
	else
	{
		COLOR.a = evalDF(source.r, threshold, smoothness);
	}
	COLOR.a*=modulate.a;
}
