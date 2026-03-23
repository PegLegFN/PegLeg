shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
varying vec4 modulate;

void vertex() {
	modulate = COLOR;
}

void fragment() {
	vec2 texRes=1.0/TEXTURE_PIXEL_SIZE;
	vec2 adaptedUV = adaptToAspect(ControlSize, vec2(0.5,0.5),texRes,UV,true);
	adaptedUV.x = mod(adaptedUV.x, 1.0);
	adaptedUV.y = mod(adaptedUV.y+TIME*0.1, 1.0);
	COLOR.a=1.0-distance(UV*vec2(2.0,1.0),vec2(1.0,1.0));
	COLOR.a*=(texture(TEXTURE, adaptedUV).a*0.7)+0.3;
	float invert = 1.0-COLOR.a;
	COLOR.a = 1.0-(invert*invert*invert);
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
