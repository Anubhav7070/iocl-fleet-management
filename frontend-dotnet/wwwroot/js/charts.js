window.charts = {
    doughnutChart: null,
    barChart: null,

    createDoughnut: function (canvasId, data, labels) {
        var canvas = document.getElementById(canvasId);
        if (!canvas) return;

        if (this.doughnutChart) {
            this.doughnutChart.destroy();
        }

        var ctx = canvas.getContext('2d');
        this.doughnutChart = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: ['#16A34A', '#EAB308', '#EA580C', '#DC2626'],
                    borderWidth: 1,
                    borderColor: '#ffffff'
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: {
                            font: { size: 10, weight: 'bold' },
                            boxWidth: 12
                        }
                    }
                }
            }
        });
    },

    createBar: function (canvasId, data, labels) {
        var canvas = document.getElementById(canvasId);
        if (!canvas) return;

        if (this.barChart) {
            this.barChart.destroy();
        }

        var ctx = canvas.getContext('2d');
        this.barChart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Compliance Index %',
                    data: data,
                    backgroundColor: '#0054A6',
                    hoverBackgroundColor: '#FF6B00',
                    borderRadius: 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false }
                },
                scales: {
                    y: {
                        min: 0,
                        max: 100,
                        ticks: { font: { size: 9, weight: 'bold' } }
                    },
                    x: {
                        ticks: { font: { size: 9, weight: 'bold' } }
                    }
                }
            }
        });
    }
};
