shader_type canvas_item;
#include "Includes/ShadowEffect.gdshaderinc"

uniform float inset:hint_range(0,1)=0;
uniform float shade:hint_range(0,1)=0.2;
uniform float shadeAlpha:hint_range(0,1)=0.8;

void fragment() {
	if(inset>0.0)
	{
		COLOR = texture(TEXTURE, ((UV-vec2(inset*0.5))/(1.0-inset)));
	}
	COLOR=AddShadow(COLOR, UV, shade, shadeAlpha);
}
