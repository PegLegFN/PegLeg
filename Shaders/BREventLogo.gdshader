shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/ScaleVector.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 ShiftDirection = vec2(0.5,0.5);
uniform float Scale = 1.0;

void fragment() {
	vec2 texRes=1.0/TEXTURE_PIXEL_SIZE;
	vec2 adaptedUV = adaptToAspect(ControlSize, ShiftDirection,texRes,UV,false);
	adaptedUV = scaleVector2(adaptedUV, vec2(Scale), vec2(0.5));
	
	COLOR = texture(TEXTURE, adaptedUV);
	
	float test = 1.0-(distance(UV, vec2(0.5))*2.0);
	test*=10.0;
	COLOR.a*=test;
}
