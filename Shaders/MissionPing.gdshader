shader_type canvas_item;

varying vec4 modulate;
void vertex() {
	modulate = COLOR;
}

void fragment() {
	float dist = clamp(1.0-distance(UV, vec2(0.5))*2.0, 0, 1);
	float factor = pow(dist, 1.5);
	factor = 6.0-factor;
	COLOR = modulate;
	COLOR.a = (sin(factor*50.0 + TIME*10.0)*8.0-4.0f)*dist * max(1.0-dist*2.0, 0);
	//COLOR.a = dist;
}
