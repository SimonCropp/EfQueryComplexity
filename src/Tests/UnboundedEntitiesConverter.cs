// UnboundedEntities has no public instance members, so it is written as its description
class UnboundedEntitiesConverter :
    WriteOnlyJsonConverter<UnboundedEntities>
{
    public override void Write(VerifyJsonWriter writer, UnboundedEntities value) =>
        writer.WriteValue(value.ToString());
}
