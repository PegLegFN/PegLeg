shader_type canvas_item;

uniform float speed : hint_range(0,50) = 1;
uniform float size : hint_range(0,1) = 1;
uniform float amp : hint_range(0,1) = 1;
uniform bool flip = false;

void fragment() {
	float dir = (flip?1.0:-1.0);
	float boost = distance(UV.y,0.5)*size*dir*-0.5;
	COLOR.rgb*=sin((UV.x+boost*0.1)/(dir*size/100.0)+TIME*speed)*(1.0-amp)+(amp);
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
