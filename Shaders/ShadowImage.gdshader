shader_type canvas_item;
#include "Includes/ShadowEffect.gdshaderinc"

uniform float inset:hint_range(0,1)=0;
uniform float shade:hint_range(0,1)=0.2;
uniform float shadeAlpha:hint_range(0,1)=0.8;

uniform float base=0.0;
uniform float scalar=2.0;
uniform float power=2.0;

void fragment() {
	if(inset>0.0)
	{
		COLOR = texture(TEXTURE, ((UV-vec2(inset*0.5))/(1.0-inset)));
	}
	COLOR=AddShadowCustom(COLOR, UV, shade, shadeAlpha, base, scalar, power);
}
