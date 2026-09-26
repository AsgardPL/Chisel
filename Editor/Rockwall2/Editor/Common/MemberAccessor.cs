using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common;
public readonly struct MemberAccessor
{
    private readonly Func<object, object> getter;
    private readonly Action<object, object> setter;

    public Type MemberType { get; }
    public string Name { get; }
    public Chisel.Models.EditorFieldAttribute Attribute { get; }

    public MemberAccessor(FieldInfo field, Chisel.Models.EditorFieldAttribute attr)
    {
        MemberType = field.FieldType;
        Name = field.Name;
        Attribute = attr;
        getter = field.GetValue;
        setter = field.SetValue;
    }

    public MemberAccessor(PropertyInfo prop, Chisel.Models.EditorFieldAttribute attr)
    {
        MemberType = prop.PropertyType;
        Name = prop.Name;
        Attribute = attr;
        getter = prop.GetValue;
        setter = prop.CanWrite ? prop.SetValue : (obj, val) => { };
    }

    public object Get(object target) => getter(target);
    public void Set(object target, object value) => setter(target, value);
}