// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace System.Linq.Expressions;

/// <summary>
/// Extensions for lambda expressions.
/// </summary>
static class LambdaExtensions
{
    /// <summary>
    /// Wrapper around <see cref="Expression.AndAlso(Expression, Expression)"/>.
    /// </summary>
    /// <typeparam name="TFunc">The func type of the expression.</typeparam>
    /// <param name="left">The left expression.</param>
    /// <param name="right">The right expression.</param>
    /// <returns>A new expression with left and right and'ed together.</returns>
    public static Expression<TFunc>? AndAlso<TFunc>(Expression<TFunc>? left, Expression<TFunc>? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Expression.Lambda<TFunc>(Expression.AndAlso(left.Body, right.Body), left.Parameters[0]);
    }

    /// <summary>
    /// Wrapper around <see cref="Expression.OrElse(Expression, Expression)"/>.
    /// </summary>
    /// <typeparam name="TFunc">The func type of the expression.</typeparam>
    /// <param name="left">The left expression.</param>
    /// <param name="right">The right expression.</param>
    /// <returns>A new expression with left and right or'ed together.</returns>
    public static Expression<TFunc>? OrElse<TFunc>(Expression<TFunc>? left, Expression<TFunc>? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Expression.Lambda<TFunc>(Expression.OrElse(left.Body, right.Body), left.Parameters[0]);
    }
}
