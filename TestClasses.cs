namespace GFileWriter
{
  public class BaseClass
  {
    public string Name { get; set; }
  }

  public class DerivedClass1 : BaseClass
  {
    public DerivedClass1()
    {
      Name = "DerivedClass1";
      DerivedProperty = "DerivedProperty";
    }

    public string DerivedProperty { get; set; }
  }

  public class DerivedClass2 : BaseClass
  {
    public DerivedClass2()
    {
      Name = "DerivedClass2";
      OtherDerivedProperty = "OtherDerivedProperty";
    }

    public string OtherDerivedProperty { get; set; }
  }
}
