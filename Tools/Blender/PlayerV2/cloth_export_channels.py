"""Preserve authored metre-valued Cloth constraints through FBX's float UV stream."""
def preserve_cloth_mobility(objects):
    records=[]
    for obj in objects:
        attribute=obj.data.color_attributes.get('ClothMobility')
        if attribute is None or not obj.name.startswith(('DosaV2_Robe_','DosaV2_SleeveOuter_')):continue
        assert attribute.domain=='POINT'
        while len(obj.data.uv_layers)<4:obj.data.uv_layers.new(name='Reserved_Channel_'+str(len(obj.data.uv_layers)))
        layer=obj.data.uv_layers[3];layer.name='ClothMobility_Meters'
        for loop in obj.data.loops:layer.data[loop.index].uv=(attribute.data[loop.vertex_index].color[0],0.)
        obj['ClothMobilityTransport']='UV3.x in metres; UV0 material coordinates unchanged; red vertex colors are preview only.'
        records.append({'mesh':obj.name,'vertices':len(obj.data.vertices),'exactPins':sum(p.color[0]==0 for p in attribute.data)})
    return records
