shader_type canvas_item;
uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float ratio : hint_range(0.0, 15.0) = 0.5f;
uniform float offset : hint_range(0.0, 15.0) = 0.1f;
uniform float scalingRange = 15;
uniform float posThreshold = 15;
uniform float posPushdown : hint_range(0.0, 1.0) = 0.095f;

varying vec4 modulate;

void vertex()
{
	modulate=COLOR;
}

void fragment() {
	float pos = ControlSize.x*(1.0-UV.x);
	float pushdown = clamp(pos/posThreshold,0,1)*posPushdown;
	
	float realRange = clamp(pos,0,1);
	float realRatio = mix(0,ratio/ControlSize.x,UV.x);
	float realOffset = mix(0,offset/ControlSize.x,UV.x);
	
	float result = ((UV.x+UV.y*realRatio)-realRatio)+realOffset;
	result = result>0.99 ? 0.0:result;
	result = UV.y>pushdown ? result : 0.0;
	
	COLOR = modulate;
	COLOR.a*=result;
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
