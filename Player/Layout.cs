using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using UIKit;
using System.Reflection;
using Foundation;
using CoreGraphics;

namespace Praeclarum.UI
{
	public static class Layout
	{
		/// <summary>
		/// <para>Constrains the layout of subviews according to equations and
		/// inequalities specified in <paramref name="constraints"/>.  Issue
		/// multiple constraints per call using the &amp;&amp; operator.</para>
		/// <para>e.g. button.Frame.Left &gt;= text.Frame.Right + 22 &amp;&amp;
		/// button.Frame.Width == View.Frame.Width * 0.42f</para>
		/// </summary>
		/// <param name="view">The superview laying out the referenced subviews.</param>
		/// <param name="constraints">Constraint equations and inequalities.</param>
		public static NSLayoutConstraint[] ConstrainLayout (this UIView view, Expression<Func<bool>> constraints)
		{
			return ConstrainLayout (view, constraints, UILayoutPriority.Required);
		}

		/// <summary>
		/// <para>Constrains the layout of subviews according to equations and
		/// inequalities specified in <paramref name="constraints"/>.  Issue
		/// multiple constraints per call using the &amp;&amp; operator.</para>
		/// <para>e.g. button.Frame.Left &gt;= text.Frame.Right + 22 &amp;&amp;
		/// button.Frame.Width == View.Frame.Width * 0.42f</para>
		/// </summary>
		/// <param name="view">The superview laying out the referenced subviews.</param>
		/// <param name="constraints">Constraint equations and inequalities.</param>
		/// <param name = "priority">The priority of the constraints</param>
		public static NSLayoutConstraint[] ConstrainLayout (this UIView view, Expression<Func<bool>> constraints, UILayoutPriority priority)
		{
			var body = constraints.Body;

			var exprs = new List<BinaryExpression> ();
			FindConstraints (body, exprs);

			var layoutConstraints = exprs.Select (e => CompileConstraint (e, view)).ToArray ();

			if (layoutConstraints.Length > 0) {
				foreach (var c in layoutConstraints) {
					c.Priority = (float)priority;
				}
				view.AddConstraints (layoutConstraints);
			}

			return layoutConstraints;
		}

		static NSLayoutConstraint CompileConstraint (BinaryExpression expr, UIView constrainedView)
		{
			var rel = NSLayoutRelation.Equal;
			switch (expr.NodeType) {
			case ExpressionType.Equal:
				rel = NSLayoutRelation.Equal;
				break;
			case ExpressionType.LessThanOrEqual:
				rel = NSLayoutRelation.LessThanOrEqual;
				break;
			case ExpressionType.GreaterThanOrEqual:
				rel = NSLayoutRelation.GreaterThanOrEqual;
				break;
			default:
				throw new NotSupportedException ("Not a valid relationship for a constrain.");
			}

			var left = GetViewAndAttribute (expr.Left);
			if (left.Item1 != constrainedView) {
				left.Item1.TranslatesAutoresizingMaskIntoConstraints = false;
			}

			var right = GetRight (expr.Right);

			return NSLayoutConstraint.Create (
				left.Item1, left.Item2,
				rel,
				right.Item1, right.Item2,
				right.Item3, right.Item4);
		}

		static Tuple<UIView, NSLayoutAttribute, float, float> GetRight (Expression expr)
		{
			var r = expr;

			UIView view = null;
			NSLayoutAttribute attr = NSLayoutAttribute.NoAttribute;
			var mul = 1.0f;
			var add = 0.0f;
			var pos = true;

			if (r.NodeType == ExpressionType.Add || r.NodeType == ExpressionType.Subtract) {
				var rb = (BinaryExpression)r;
				if (IsConstant (rb.Left)) {
					add = ConstantValue (rb.Left);
					if (r.NodeType == ExpressionType.Subtract) {
						pos = false;
					}
					r = rb.Right;
				}
				else if (IsConstant (rb.Right)) {
					add = ConstantValue (rb.Right);
					if (r.NodeType == ExpressionType.Subtract) {
						add = -add;
					}
					r = rb.Left;
				}
				else {
					throw new NotSupportedException ("Addition only supports constants: " + rb.Right.NodeType);
				}
			}

			if (r.NodeType == ExpressionType.Multiply) {
				var rb = (BinaryExpression)r;
				if (IsConstant (rb.Left)) {
					mul = ConstantValue (rb.Left);
					r = rb.Right;
				}
				else if (IsConstant (rb.Right)) {
					mul = ConstantValue (rb.Right);
					r = rb.Left;
				}
				else {
					throw new NotSupportedException ("Multiplication only supports constants.");
				}
			}

			if (IsConstant (r)) {
				add = Convert.ToSingle (Eval (r));
			} else if (r.NodeType == ExpressionType.MemberAccess || r.NodeType == ExpressionType.Call) {
				var t = GetViewAndAttribute (r);
				view = t.Item1;
				attr = t.Item2;
			} else {
				throw new NotSupportedException ("Unsupported layout expression node type " + r.NodeType);
			}

			if (!pos)
				mul = -mul;

			return Tuple.Create (view, attr, mul, add);
		}

		static bool IsConstant (Expression expr)
		{
			if (expr.NodeType == ExpressionType.Constant)
				return true;

			if (expr.NodeType == ExpressionType.MemberAccess) {
				var mexpr = (MemberExpression)expr;
				var m = mexpr.Member;
				if (m.MemberType == MemberTypes.Field) {
					return true;
				}
				return false;
			}

			if (expr.NodeType == ExpressionType.Convert) {
				var cexpr = (UnaryExpression)expr;
				return IsConstant (cexpr.Operand);
			}

			return false;
		}

		static float ConstantValue (Expression expr)
		{
			return Convert.ToSingle (Eval (expr));
		}

		static Tuple<UIView, NSLayoutAttribute> GetViewAndAttribute (Expression expr)
		{
			var attr = NSLayoutAttribute.NoAttribute;
			MemberExpression frameExpr = null;

			var fExpr = expr as MethodCallExpression;
			if (fExpr != null) {
				switch (fExpr.Method.Name) {
				case "GetMidX":
				case "GetCenterX":
					attr = NSLayoutAttribute.CenterX;
					break;
				case "GetMidY":
				case "GetCenterY":
					attr = NSLayoutAttribute.CenterY;
					break;
				case "GetBaseline":
					attr = NSLayoutAttribute.Baseline;
					break;
				default:
					throw new NotSupportedException ("Method " + fExpr.Method.Name + " is not recognized.");
				}

				frameExpr = fExpr.Arguments.FirstOrDefault () as MemberExpression;
			}

			if (attr == NSLayoutAttribute.NoAttribute) {
				var memExpr = expr as MemberExpression;
				if (memExpr == null)
					throw new NotSupportedException ("Left hand side of a relation must be a member expression, instead it is " + expr);

				switch (memExpr.Member.Name) {
					case "Width":
					attr = NSLayoutAttribute.Width;
					break;
					case "Height":
					attr = NSLayoutAttribute.Height;
					break;
					case "Left":
					case "X":
					attr = NSLayoutAttribute.Left;
					break;
					case "Top":
					case "Y":
					attr = NSLayoutAttribute.Top;
					break;
					case "Right":
					attr = NSLayoutAttribute.Right;
					break;
					case "Bottom":
					attr = NSLayoutAttribute.Bottom;
					break;
					default:
					throw new NotSupportedException ("Property " + memExpr.Member.Name + " is not recognized.");
				}

				frameExpr = memExpr.Expression as MemberExpression;
			}

			if (frameExpr == null)
				throw new NotSupportedException ("Constraints should use the Frame or Bounds property of views.");
			var viewExpr = frameExpr.Expression;

			var view = Eval (viewExpr) as UIView;
			if (view == null)
				throw new NotSupportedException ("Constraints only apply to views.");

			return Tuple.Create (view, attr);
		}

		static object Eval (Expression expr)
		{
			if (expr.NodeType == ExpressionType.Constant) {
				return ((ConstantExpression)expr).Value;
			}
			
			if (expr.NodeType == ExpressionType.MemberAccess) {
				var mexpr = (MemberExpression)expr;
				var m = mexpr.Member;
				if (m.MemberType == MemberTypes.Field) {
					var f = (FieldInfo)m;
					var v = f.GetValue (Eval (mexpr.Expression));
					return v;
				}
			}

			if (expr.NodeType == ExpressionType.Convert) {
				var cexpr = (UnaryExpression)expr;
				var op = Eval (cexpr.Operand);
				if (cexpr.Type == typeof(nfloat))
					return (nfloat)Convert.ToSingle(op);
				else
					return Convert.ChangeType(op, cexpr.Type);
			}

			return Expression.Lambda (expr).Compile ().DynamicInvoke ();
		}

		static void FindConstraints (Expression expr, List<BinaryExpression> constraintExprs)
		{
			var b = expr as BinaryExpression;
			if (b == null)
				return;

			if (b.NodeType == ExpressionType.AndAlso) {
				FindConstraints (b.Left, constraintExprs);
				FindConstraints (b.Right, constraintExprs);
			} else {
				constraintExprs.Add (b);
			}
		}

		/// <summary>
		/// The baseline of the view whose frame is viewFrame. Use only when defining constraints.
		/// </summary>
		public static float GetBaseline (this CGRect viewFrame)
		{
			return 0;
		}

		/// <summary>
		/// The x coordinate of the center of the frame.
		/// </summary>
		public static float GetCenterX (this CGRect viewFrame)
		{
			return (float)(viewFrame.X + viewFrame.Width / 2);
		}

		/// <summary>
		/// The y coordinate of the center of the frame.
		/// </summary>
		public static float GetCenterY (this CGRect viewFrame)
		{
			return (float)(viewFrame.Y + viewFrame.Height / 2);
		}
	}
}
