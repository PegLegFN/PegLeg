shader_type canvas_item;

uniform float scalar = 2.0;
uniform float base = 1.0;

void fragment() {
	COLOR.a*=base-(distance(vec2(0.5),UV)*scalar);
}
