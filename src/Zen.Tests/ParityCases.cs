using System.Text;

namespace Zen.Tests;

/// <summary>
/// The shared battery of expressions used to prove managed and native produce
/// identical results. Each row is (name, expression, contextJson).
/// Spans simple/complex logic and few/many input parameters.
/// </summary>
public static class ParityCases
{
    public static IEnumerable<object[]> All => StaticCases().Concat(GeneratedCases());

    private static IEnumerable<object[]> StaticCases()
    {
        yield return Row("arith-basic", "1 + 2 * 3", "{}");
        yield return Row("arith-paren", "(1 + 2) * 3", "{}");
        yield return Row("arith-div", "10 / 4", "{}");
        yield return Row("arith-mod", "7 % 3", "{}");
        yield return Row("arith-pow", "2 ^ 10", "{}");
        yield return Row("arith-unary", "-5 + 3", "{}");
        yield return Row("arith-mixed", "((price * quantity) - discount) * (1 + tax)", "{\"price\":10,\"quantity\":3,\"discount\":5,\"tax\":0.2}");

        yield return Row("logic-not", "not true", "{}");
        yield return Row("logic-and", "true and false", "{}");
        yield return Row("logic-or", "true or false", "{}");
        yield return Row("logic-chain", "1 < 2 and 3 > 2 or false", "{}");

        yield return Row("cmp-eq", "5 == 5", "{}");
        yield return Row("cmp-ne", "5 != 3", "{}");
        yield return Row("cmp-le", "3 <= 3", "{}");
        yield return Row("cmp-str", "'banana' < 'cherry'", "{}");

        yield return Row("ternary-true", "x > 10 ? 'big' : 'small'", "{\"x\":20}");
        yield return Row("ternary-false", "x > 10 ? 'big' : 'small'", "{\"x\":5}");
        yield return Row("ternary-nested", "score >= 90 ? 'A' : score >= 80 ? 'B' : score >= 70 ? 'C' : 'F'", "{\"score\":85}");

        yield return Row("coalesce-second", "a ?? b", "{\"a\":null,\"b\":7}");
        yield return Row("coalesce-first", "a ?? b", "{\"a\":3,\"b\":7}");
        yield return Row("coalesce-chain", "a ?? b ?? c", "{\"a\":null,\"b\":null,\"c\":9}");
        yield return Row("coalesce-in-expr", "base + (bonus ?? 0)", "{\"base\":100,\"bonus\":null}");

        yield return Row("str-concat", "'Hello ' + name", "{\"name\":\"World\"}");
        yield return Row("str-concat-num", "'Order #' + string(orderId)", "{\"orderId\":12345}");

        yield return Row("access-field", "price * quantity", "{\"price\":2.5,\"quantity\":4}");
        yield return Row("access-nested", "user.profile.name", "{\"user\":{\"profile\":{\"name\":\"Ada\"}}}");
        yield return Row("access-index", "items[1]", "{\"items\":[10,20,30]}");
        yield return Row("access-index-neg", "items[-1]", "{\"items\":[10,20,30]}");
        yield return Row("access-missing", "missing", "{}");
        yield return Row("access-missing-deep", "obj.missing", "{\"obj\":{}}");

        yield return Row("fn-sum", "sum([1,2,3,4])", "{}");
        yield return Row("fn-len-str", "len('hello')", "{}");
        yield return Row("fn-len-arr", "len([1,2,3])", "{}");
        yield return Row("fn-round", "round(3.14159, 2)", "{}");
        yield return Row("fn-upper", "upper('abc')", "{}");
        yield return Row("fn-max", "max([3,1,2])", "{}");
        yield return Row("fn-min", "min([3,1,2])", "{}");
        yield return Row("fn-abs", "abs(-7)", "{}");
        yield return Row("fn-floor", "floor(3.9)", "{}");
        yield return Row("fn-ceil", "ceil(3.1)", "{}");
        yield return Row("fn-contains", "contains('hello world', 'world')", "{}");
        yield return Row("fn-concat", "concat('a','b','c')", "{}");
        yield return Row("fn-replace", "replace('a-b-c', '-', '_')", "{}");
        yield return Row("fn-substring", "substring('sample_string', 7)", "{}");

        yield return Row("closure-map-squared", "map([1,2,3], # ^ 2)", "{}");
        yield return Row("closure-map-sum", "sum(map([1,2,3], # * 2))", "{}");
        yield return Row("closure-filter", "filter([1,2,3,4,5], # > 2)", "{}");
        yield return Row("closure-all", "all([2,4,6], # % 2 == 0)", "{}");
        yield return Row("closure-some", "some([1,3,5], # > 4)", "{}");
        yield return Row("closure-cart", "sum(map(cart, #.price * #.qty))", "{\"cart\":[{\"price\":2,\"qty\":3},{\"price\":5,\"qty\":2}]}");

        yield return Row("in-array-yes", "x in [1,2,3]", "{\"x\":2}");
        yield return Row("in-array-no", "x in [1,2,3]", "{\"x\":5}");
        yield return Row("in-range-incl", "x in [1..10]", "{\"x\":7}");
        yield return Row("in-range-end", "x in [1..10]", "{\"x\":10}");
        yield return Row("in-range-excl-low", "x in (0..100)", "{\"x\":0}");
        yield return Row("in-range-mixed", "x in [0..100)", "{\"x\":100}");
        yield return Row("in-range-notin", "x not in [5..10]", "{\"x\":50}");
        yield return Row("in-strings", "'US' in ['US','CA','GB']", "{}");
        yield return Row("in-strings-ctx", "status in ['active','pending']", "{\"status\":\"active\"}");
        yield return Row("in-string-contains", "needle in haystack", "{\"needle\":\"ell\",\"haystack\":\"hello\"}");

        yield return Row("complex-discount", "order.total > 100 ? order.total * 0.9 : order.total", "{\"order\":{\"total\":150}}");
        yield return Row("complex-filter-count", "len(filter(scores, # >= threshold)) >= minPass", "{\"scores\":[40,60,80,90],\"threshold\":60,\"minPass\":2}");
        yield return Row("object-literal", "{a: 1, b: x + 1}", "{\"x\":5}");
        yield return Row("computed-index", "grades[score >= 90 ? 'high' : 'low']", "{\"grades\":{\"high\":\"A\",\"low\":\"C\"},\"score\":95}");
        yield return Row("string-template-via-concat", "'Hi ' + first + ' ' + last + '!'", "{\"first\":\"Ada\",\"last\":\"Lovelace\"}");
    }

    /// <summary>Generated cases with many input parameters.</summary>
    private static IEnumerable<object[]> GeneratedCases()
    {
        const int n = 50;

        // Sum of 50 scalar parameters.
        var sbExpr = new StringBuilder();
        var sbCtx = new StringBuilder();
        sbCtx.Append('{');
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sbExpr.Append('+');
            sbExpr.Append("v").Append(i);
            if (i > 0) sbCtx.Append(',');
            sbCtx.Append("\"v").Append(i).Append("\":").Append(i + 1);
        }
        sbCtx.Append('}');
        yield return Row("many-sum-50", sbExpr.ToString(), sbCtx.ToString());

        // Logical AND of 50 comparisons.
        var sbAnd = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sbAnd.Append(" and ");
            sbAnd.Append("v").Append(i).Append(" > 0");
        }
        yield return Row("many-logic-50", sbAnd.ToString(), sbCtx.ToString());

        // Weighted sum referencing 10 nested object fields.
        var sbObjExpr = new StringBuilder();
        var sbObjCtx = new StringBuilder();
        sbObjCtx.Append("{\"data\":{");
        int m = 10;
        for (int i = 0; i < m; i++)
        {
            if (i > 0) sbObjExpr.Append('+');
            sbObjExpr.Append("data.f").Append(i);
            if (i > 0) sbObjCtx.Append(',');
            sbObjCtx.Append("\"f").Append(i).Append("\":").Append((i + 1) * (i + 1));
        }
        sbObjCtx.Append("}}");
        yield return Row("many-fields-10", sbObjExpr.ToString(), sbObjCtx.ToString());

        // A big nested ternary over a single score is covered above; here a wide
        // arithmetic expression mixing functions and many params.
        var sbWide = new StringBuilder("round((");
        for (int i = 0; i < m; i++)
        {
            if (i > 0) sbWide.Append('+');
            sbWide.Append("data.f").Append(i).Append(" * ").Append(i + 1);
        }
        sbWide.Append(") / 10, 2)");
        yield return Row("many-wide-arith", sbWide.ToString(), sbObjCtx.ToString());
    }

    private static object[] Row(string name, string expr, string ctx) => new object[] { name, expr, ctx };
}
