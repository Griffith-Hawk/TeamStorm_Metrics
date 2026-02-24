const btn = document.getElementById('loadBtn');
const output = document.getElementById('output');

btn?.addEventListener('click', async () => {
    output.textContent = 'Загрузка...';
    try {
        const response = await fetch('/api/workspaces');
        const data = await response.json();
        output.textContent = JSON.stringify(data, null, 2);
    } catch (error) {
        output.textContent = `Ошибка: ${error}`;
    }
});
