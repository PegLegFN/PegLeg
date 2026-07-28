shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"
#include "Includes/RotateVector.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float CornerRadius = 10.0;
uniform float time = 0.0;

varying vec4 modulate;

void vertex()
{
	modulate=COLOR;
}

void fragment() {
	vec2 texRes=1.0/TEXTURE_PIXEL_SIZE;
	vec2 adaptedUV = adaptToAspect(ControlSize, vec2(0.5),texRes,UV,false);
	COLOR = texture(TEXTURE, rotateVector2(adaptedUV, 72.0*time, vec2(0.5))); // 360/5=72
	COLOR.a *= cornerMask(UV,CornerRadius,ControlSize);
	COLOR*=modulate;
}
