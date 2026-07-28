shader_type canvas_item;

uniform vec3 from : source_color = vec3(1,0,0);
uniform vec3 to : source_color = vec3(1,1,0);
uniform float speed : hint_range(1.0, 30.0) = 7;
void vertex() {
	COLOR.rgb = mix(from,to,(sin(mod(TIME*speed, PI))));
}

void fragment() {
	// Called for every pixel the material is visible on.
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
