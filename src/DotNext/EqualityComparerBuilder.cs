using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotNext;

using Reflection;
using Intrinsics = Runtime.Intrinsics;
using Seq = Collections.Generic.Sequence;

/// <summary>
/// Generates hash code and equality check functions for the particular type.
/// </summary>
/// <typeparam name="T">The type for which equality check and hash code functions should be generated.</typeparam>
/// <remarks>
/// Automatically generated hash code and equality check functions can be used
/// instead of manually written implementation of overridden <see cref="object.GetHashCode"/> and <see cref="object.Equals(object)"/> methods.
/// </remarks>
public readonly struct EqualityComparerBuilder<T>
{
    private const BindingFlags PublicStaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly IReadOnlySet<string>? excludedFields;

    /// <summary>
    /// Sets an array of excluded field names.
    /// </summary>
    /// <value>An array of excluded fields.</value>
    public string[] ExcludedFields
    {
        init => excludedFields = new HashSet<string>(value);
    }

    private bool IsIncluded(FieldInfo field) => excludedFields?.Contains(field.Name) ?? true;

    /// <summary>
    /// Set a value indicating that hash code must be unique for each application instance.
    /// </summary>
    /// <value><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</value>
    public bool SaltedHashCode
    {
        private get;
        init;
    }

    private sealed class ConstructedEqualityComparer : IEqualityComparer<T>
    {
        private readonly Func<T?, T?, bool> equality;
        private readonly Func<T, int> hashCode;

        internal ConstructedEqualityComparer(Func<T?, T?, bool> equality, Func<T, int> hashCode)
        {
            this.equality = equality;
            this.hashCode = hashCode;
        }

        bool IEqualityComparer<T>.Equals(T? x, T? y) => equality(x, y);

        int IEqualityComparer<T>.GetHashCode(T obj) => hashCode(obj);
    }

    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    private static MethodCallExpression EqualsMethodForValueType(MemberExpression first, MemberExpression second)
    {
        var method = typeof(BitwiseComparer<>)
            .MakeGenericType(first.Type)
            .GetMethod(nameof(BitwiseComparer<int>.Equals), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            ?.MakeGenericMethod(second.Type);

        Debug.Assert(method is not null);
        return Expression.Call(method, first, second);
    }

    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    private static MethodCallExpression HashCodeMethodForValueType(Expression expr, ConstantExpression salted)
    {
        var method = typeof(BitwiseComparer<>)
            .MakeGenericType(expr.Type)
            .GetMethod(nameof(BitwiseComparer<int>.GetHashCode), 0, new[] { expr.Type.MakeByRefType(), typeof(bool) });

        Debug.Assert(method is not null);
        return Expression.Call(method, expr, salted);
    }

    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    private static MethodInfo EqualsMethodForArrayElementType(Type itemType)
    {
        var arrayType = Type.MakeGenericMethodParameter(0).MakeArrayType();
        return itemType.IsValueType ?
            typeof(OneDimensionalArray)
                    .GetMethod(nameof(OneDimensionalArray.BitwiseEquals), 1, PublicStaticFlags, null, new[] { arrayType, arrayType }, null)!
                    .MakeGenericMethod(itemType)
            : new Func<IEnumerable<object>?, IEnumerable<object>?, bool>(Seq.SequenceEqual).Method;
    }

    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    private static MethodCallExpression EqualsMethodForArrayElementType(MemberExpression fieldX, MemberExpression fieldY)
    {
        Debug.Assert(fieldX.Type.IsSZArray);
        var method = EqualsMethodForArrayElementType(fieldX.Type.GetElementType()!);
        return Expression.Call(method, fieldX, fieldY);
    }

    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    private static MethodInfo HashCodeMethodForArrayElementType(Type itemType)
    {
        var arrayType = Type.MakeGenericMethodParameter(0).MakeArrayType();
        var result = itemType.IsValueType ?
              typeof(OneDimensionalArray)
                      .GetMethod(nameof(OneDimensionalArray.BitwiseHashCode), 1, PublicStaticFlags, null, new[] { arrayType, typeof(bool) }, null)!
                      .MakeGenericMethod(itemType) :
              typeof(Seq)
                      .GetMethod(nameof(Seq.SequenceHashCode), new[] { typeof(IEnumerable<object>), typeof(bool) });

        Debug.Assert(result is not null);
        return result;
    }

    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    private static MethodCallExpression HashCodeMethodForArrayElementType(Expression expr, ConstantExpression salted)
    {
        Debug.Assert(expr.Type.IsSZArray);
        var method = HashCodeMethodForArrayElementType(expr.Type.GetElementType()!);
        return Expression.Call(method, expr, salted);
    }

    private static IEnumerable<FieldInfo> GetAllFields(Type type)
    {
        foreach (var t in type.GetBaseTypes(includeTopLevel: true))
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic))
                yield return field;
        }
    }

    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    private Func<T?, T?, bool> BuildEquals()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException();
        var x = Expression.Parameter(typeof(T));
        if (x.Type.IsPrimitive)
            return EqualityComparer<T?>.Default.Equals;
        if (x.Type.IsSZArray)
            return EqualsMethodForArrayElementType(x.Type.GetElementType()!).CreateDelegate<Func<T?, T?, bool>>();

        var y = Expression.Parameter(x.Type);

        // collect all fields in the hierarchy
        Expression? expr = x.Type.IsClass ? Expression.ReferenceNotEqual(y, Expression.Constant(null, y.Type)) : null;
        foreach (var field in GetAllFields(x.Type))
        {
            if (IsIncluded(field))
            {
                var fieldX = Expression.Field(x, field);
                var fieldY = Expression.Field(y, field);
                Expression condition = field.FieldType switch
                {
                    { IsPointer: true } or { IsPrimitive: true } or { IsEnum: true } => Expression.Equal(fieldX, fieldY),
                    { IsValueType: true } => EqualsMethodForValueType(fieldX, fieldY),
                    { IsSZArray: true } => EqualsMethodForArrayElementType(fieldX, fieldY),
                    _ => Expression.Call(new Func<object, object, bool>(Equals).Method, fieldX, fieldY)
                };

                expr = expr is null ? condition : Expression.AndAlso(expr, condition);
            }
        }

        if (x.Type.IsClass)
        {
            var referenceEquality = Expression.ReferenceEqual(x, y);
            expr = expr is null ? referenceEquality : Expression.OrElse(referenceEquality, expr);
        }
        else if (expr is null)
        {
            expr = Expression.Constant(true, typeof(bool));
        }

        return Expression.Lambda<Func<T?, T?, bool>>(expr, false, x, y).Compile();
    }

    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    private Func<T, int> BuildGetHashCode()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException();
        Expression expr;
        var inputParam = Expression.Parameter(typeof(T));
        if (inputParam.Type.IsPrimitive)
            return EqualityComparer<T>.Default.GetHashCode!;
        if (inputParam.Type.IsSZArray)
        {
            expr = HashCodeMethodForArrayElementType(inputParam, Expression.Constant(SaltedHashCode));
            return Expression.Lambda<Func<T, int>>(expr, true, inputParam).Compile();
        }

        var hashCodeTemp = Expression.Parameter(typeof(int));
        ICollection<Expression> expressions = new LinkedList<Expression>();

        // collect all fields in the hierarchy
        foreach (var field in GetAllFields(inputParam.Type))
        {
            if (IsIncluded(field))
            {
                expr = Expression.Field(inputParam, field);
                expr = field.FieldType switch
                {
                    { IsPointer: true } => Expression.Call(typeof(Intrinsics).GetMethod(nameof(Intrinsics.PointerHashCode), BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.Public)!, expr),
                    { IsPrimitive: true } => Expression.Call(expr, nameof(GetHashCode), Type.EmptyTypes),
                    { IsValueType: true } => HashCodeMethodForValueType(expr, Expression.Constant(SaltedHashCode)),
                    { IsSZArray: true } => HashCodeMethodForArrayElementType(expr, Expression.Constant(SaltedHashCode)),
                    _ => Expression.Condition(
                        Expression.ReferenceEqual(expr, Expression.Constant(null, expr.Type)),
                        Expression.Constant(0, typeof(int)),
                        Expression.Call(expr, nameof(GetHashCode), Type.EmptyTypes)),
                };

                expr = Expression.Assign(hashCodeTemp, Expression.Add(Expression.Multiply(hashCodeTemp, Expression.Constant(-1521134295)), expr));
                expressions.Add(expr);
            }
        }

        expressions.Add(hashCodeTemp);
        expr = Expression.Block(typeof(int), Seq.Singleton(hashCodeTemp), expressions);
        return Expression.Lambda<Func<T, int>>(expr, false, inputParam).Compile();
    }

    /// <summary>
    /// Generates implementation of <see cref="object.GetHashCode"/> and <see cref="object.Equals(object)"/> methods
    /// for particular type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="equals">The implementation of equality check.</param>
    /// <param name="hashCode">The implementation of hash code.</param>
    /// <exception cref="PlatformNotSupportedException">Dynamic code generation is not supported by underlying CLR implementation.</exception>
    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    public void Build(out Func<T?, T?, bool> equals, out Func<T, int> hashCode)
    {
        equals = BuildEquals();
        hashCode = BuildGetHashCode();
    }

    /// <summary>
    /// Generates implementation of equality comparer.
    /// </summary>
    /// <returns>The generated equality comparer.</returns>
    /// <exception cref="PlatformNotSupportedException">Dynamic code generation is not supported by underlying CLR implementation.</exception>
    [RequiresUnreferencedCode("Dynamic code generation may be incompatible with IL trimming")]
    public IEqualityComparer<T> Build()
        => typeof(T).IsPrimitive ? EqualityComparer<T>.Default : new ConstructedEqualityComparer(BuildEquals(), BuildGetHashCode());
}