shader_type canvas_item;

uniform float feather : hint_range(0.0, 1.0) = 0.1;

void fragment() {
	COLOR.a *= (1.0-(distance(UV, vec2(0.5))*2.0))/feather;
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
