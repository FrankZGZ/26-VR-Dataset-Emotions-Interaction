using UnityEngine;

public class WaterSplashEffect : MonoBehaviour
{
    public float lifeTime = 3.0f;
    public float initialScale = 0.3f;
    public float finalScale = 2.5f;
    public float randomRotationRange = 20f;

    private float timer = 0f;
    private Material splashMaterial;
    private Color originalColor;

    private AnimationCurve scaleCurve;
    private AnimationCurve alphaCurve;

    void Start()
    {
        // 初始化曲线
        scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // 缓慢扩散
        alphaCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.7f, 0.5f),
            new Keyframe(1f, 0f)
        ); // 先慢慢淡出，后面迅速消失

        // 初始化材质
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            splashMaterial = renderer.material;
            originalColor = splashMaterial.color;
        }

        // 随机初始旋转
        float randomY = Random.Range(-randomRotationRange, randomRotationRange);
        transform.rotation = Quaternion.Euler(90f, randomY, 0f); // 俯视，且随机绕Y转一点

        // 初始缩放，注意XY轴一致
        transform.localScale = new Vector3(initialScale, initialScale, initialScale);
    }

    void Update()
    {
        timer += Time.deltaTime;
        float progress = Mathf.Clamp01(timer / lifeTime);

        // 按XY轴等比例扩散
        float currentScale = Mathf.Lerp(initialScale, finalScale, scaleCurve.Evaluate(progress));
        transform.localScale = new Vector3(currentScale, currentScale, currentScale);

        // 渐渐透明
        if (splashMaterial != null)
        {
            Color color = originalColor;
            color.a *= alphaCurve.Evaluate(progress);
            splashMaterial.color = color;
        }

        // 生命结束
        if (timer >= lifeTime)
        {
            Destroy(gameObject);
        }
    }
}
