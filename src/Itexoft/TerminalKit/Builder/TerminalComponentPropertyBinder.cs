// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Linq.Expressions;
using System.Reflection;

namespace Itexoft.TerminalKit;

internal static class TerminalComponentPropertyBinder
{
    public static string GetMemberName<TComponent, TValue>(Expression<Func<TComponent, TValue>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var member = expression.Body switch
        {
            MemberExpression m => m,
            UnaryExpression { Operand: MemberExpression nested } => nested,
            _ => null
        };

        if (member?.Member is not PropertyInfo property)
            throw new ArgumentException("Expression must point to a property.", nameof(expression));

        return property.Name;
    }
}