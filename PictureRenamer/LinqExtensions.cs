namespace PictureRenamer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    public static class LinqExtensions
    {
        public static object Default(this Type type) =>
            type.GetTypeInfo().IsValueType ? Activator.CreateInstance(type) : null;

        public static IEnumerable<TResult> FullOuterJoin<TLeft, TRight, TKey, TResult>(
            this IEnumerable<TLeft> leftItems,
            IEnumerable<TRight> rightItems,
            Func<TLeft, TKey> leftKeySelector,
            Func<TRight, TKey> rightKeySelector,
            Func<TLeft, TRight, TResult> resultSelector)
            where TLeft : class
        {
            return leftItems.LeftOuterJoin(rightItems, leftKeySelector, rightKeySelector, resultSelector)
                .Concat(leftItems.RightAntiSemiJoin(rightItems, leftKeySelector, rightKeySelector, resultSelector));
        }

        public static IQueryable<TResult> FullOuterJoin<TLeft, TRight, TKey, TResult>(
            this IQueryable<TLeft> leftItems,
            IQueryable<TRight> rightItems,
            Expression<Func<TLeft, TKey>> leftKeySelector,
            Expression<Func<TRight, TKey>> rightKeySelector,
            Expression<Func<TLeft, TRight, TResult>> resultSelector)
        {
            return leftItems.LeftOuterJoin(rightItems, leftKeySelector, rightKeySelector, resultSelector)
                .Concat(leftItems.RightAntiSemiJoin(rightItems, leftKeySelector, rightKeySelector, resultSelector));
        }

        public static IEnumerable<TResult> FullOuterJoinDistinct<TLeft, TRight, TKey, TResult>(
            this IEnumerable<TLeft> leftItems,
            IEnumerable<TRight> rightItems,
            Func<TLeft, TKey> leftKeySelector,
            Func<TRight, TKey> rightKeySelector,
            Func<TLeft, TRight, TResult> resultSelector)
        {
            return leftItems.LeftOuterJoin(rightItems, leftKeySelector, rightKeySelector, resultSelector)
                .Union(leftItems.RightOuterJoin(rightItems, leftKeySelector, rightKeySelector, resultSelector));
        }

        public static IQueryable<TResult> FullOuterJoinDistinct<TLeft, TRight, TKey, TResult>(
            this IQueryable<TLeft> leftItems,
            IQueryable<TRight> rightItems,
            Expression<Func<TLeft, TKey>> leftKeySelector,
            Expression<Func<TRight, TKey>> rightKeySelector,
            Expression<Func<TLeft, TRight, TResult>> resultSelector)
        {
            return leftItems.LeftOuterJoin(rightItems, leftKeySelector, rightKeySelector, resultSelector)
                .Union(leftItems.RightOuterJoin(rightItems, leftKeySelector, rightKeySelector, resultSelector));
        }

        public static IEnumerable<TResult> LeftOuterJoin<TLeft, TRight, TKey, TResult>(
            this IEnumerable<TLeft> leftItems,
            IEnumerable<TRight> rightItems,
            Func<TLeft, TKey> leftKeySelector,
            Func<TRight, TKey> rightKeySelector,
            Func<TLeft, TRight, TResult> resultSelector)
        {
            return
                from left in leftItems
                join right in rightItems on leftKeySelector(left) equals rightKeySelector(right) into temp
                from right in temp.DefaultIfEmpty()
                select resultSelector(left, right);
        }

        public static IQueryable<TResult> LeftOuterJoin<TLeft, TRight, TKey, TResult>(
            this IQueryable<TLeft> leftItems,
            IQueryable<TRight> rightItems,
            Expression<Func<TLeft, TKey>> leftKeySelector,
            Expression<Func<TRight, TKey>> rightKeySelector,
            Expression<Func<TLeft, TRight, TResult>> resultSelector)
        {
            var sampleAnonLR = new { left = default(TLeft), rightg = (IEnumerable<TRight>)null };
            var parmP = Expression.Parameter(sampleAnonLR.GetType(), "p");
            var parmC = Expression.Parameter(typeof(TRight), "c");
            var argLeft = Expression.PropertyOrField(parmP, "left");
            var newleftrs = CastSMBody(
                Expression.Lambda(Expression.Invoke(resultSelector, argLeft, parmC), parmP, parmC),
                sampleAnonLR,
                default(TRight),
                default(TResult));

            return leftItems.AsQueryable()
                .GroupJoin(rightItems, leftKeySelector, rightKeySelector, (left, rightg) => new { left, rightg })
                .SelectMany(r => r.rightg.DefaultIfEmpty(), newleftrs);
        }

        public static IEnumerable<TResult> RightAntiSemiJoin<TLeft, TRight, TKey, TResult>(
            this IEnumerable<TLeft> leftItems,
            IEnumerable<TRight> rightItems,
            Func<TLeft, TKey> leftKeySelector,
            Func<TRight, TKey> rightKeySelector,
            Func<TLeft, TRight, TResult> resultSelector)
        {
            var hashLK = new HashSet<TKey>(from l in leftItems select leftKeySelector(l));
            return rightItems.Where(r => !hashLK.Contains(rightKeySelector(r)))
                .Select(r => resultSelector(default(TLeft), r));
        }

        public static IQueryable<TResult> RightAntiSemiJoin<TLeft, TRight, TKey, TResult>(
            this IQueryable<TLeft> leftItems,
            IQueryable<TRight> rightItems,
            Expression<Func<TLeft, TKey>> leftKeySelector,
            Expression<Func<TRight, TKey>> rightKeySelector,
            Expression<Func<TLeft, TRight, TResult>> resultSelector)
        {
            var sampleAnonLgR = new { leftg = (IEnumerable<TLeft>)null, right = default(TRight) };
            var parmLgR = Expression.Parameter(sampleAnonLgR.GetType(), "lgr");
            var argLeft = Expression.Constant(typeof(TLeft).Default(), typeof(TLeft));
            var argRight = Expression.PropertyOrField(parmLgR, "right");
            var newrightrs = CastSBody(
                Expression.Lambda(Expression.Invoke(resultSelector, argLeft, argRight), parmLgR),
                sampleAnonLgR,
                default(TResult));

            return rightItems
                .GroupJoin(leftItems, rightKeySelector, leftKeySelector, (right, leftg) => new { leftg, right })
                .Where(lgr => !lgr.leftg.Any())
                .Select(newrightrs);
        }

        public static IEnumerable<TResult> RightOuterJoin<TLeft, TRight, TKey, TResult>(
            this IEnumerable<TLeft> leftItems,
            IEnumerable<TRight> rightItems,
            Func<TLeft, TKey> leftKeySelector,
            Func<TRight, TKey> rightKeySelector,
            Func<TLeft, TRight, TResult> resultSelector)
        {
            return
                from right in rightItems
                join left in leftItems on rightKeySelector(right) equals leftKeySelector(left) into temp
                from left in temp.DefaultIfEmpty()
                select resultSelector(left, right);
        }

        public static IQueryable<TResult> RightOuterJoin<TLeft, TRight, TKey, TResult>(
            this IQueryable<TLeft> leftItems,
            IQueryable<TRight> rightItems,
            Expression<Func<TLeft, TKey>> leftKeySelector,
            Expression<Func<TRight, TKey>> rightKeySelector,
            Expression<Func<TLeft, TRight, TResult>> resultSelector)
        {
            var sampleAnonLR = new { leftg = (IEnumerable<TLeft>)null, right = default(TRight) };
            var parmP = Expression.Parameter(sampleAnonLR.GetType(), "p");
            var parmC = Expression.Parameter(typeof(TLeft), "c");
            var argRight = Expression.PropertyOrField(parmP, "right");
            var newrightrs = CastSMBody(
                Expression.Lambda(Expression.Invoke(resultSelector, parmC, argRight), parmP, parmC),
                sampleAnonLR,
                default(TLeft),
                default(TResult));

            return rightItems
                .GroupJoin(leftItems, rightKeySelector, leftKeySelector, (right, leftg) => new { leftg, right })
                .SelectMany(l => l.leftg.DefaultIfEmpty(), newrightrs);
        }

        private static Expression<Func<TP, TResult>> CastSBody<TP, TResult>(
            LambdaExpression ex,
            TP unusedP,
            TResult unusedRes) =>
            (Expression<Func<TP, TResult>>)ex;

        private static Expression<Func<TP, TC, TResult>> CastSMBody<TP, TC, TResult>(
            LambdaExpression ex,
            TP unusedP,
            TC unusedC,
            TResult unusedRes) =>
            (Expression<Func<TP, TC, TResult>>)ex;
    }
}